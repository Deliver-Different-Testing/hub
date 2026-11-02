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
