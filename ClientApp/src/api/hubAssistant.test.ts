import { http as mswHttp, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { antiforgeryHandler } from '../test/handlers';
import { setUnauthorizedHandler } from './client';
import { getAssistantEnabled, streamChat } from './hub';
import type { StreamEvent } from './types';

const sse = (...events: object[]) =>
  new HttpResponse(events.map((e) => `data: ${JSON.stringify(e)}\n\n`).join(''), { headers: { 'Content-Type': 'text/event-stream' } });

async function collect(request = { messages: [{ role: 'user', content: 'Hi' }] }): Promise<StreamEvent[]> {
  const events: StreamEvent[] = [];
  for await (const e of streamChat(request, new AbortController().signal)) events.push(e);
  return events;
}

describe('getAssistantEnabled', () => {
  it('reads the enabled flag', async () => {
    server.use(mswHttp.get('/api/assistant/status', () => HttpResponse.json({ enabled: true })));
    expect(await getAssistantEnabled()).toBe(true);
  });

  it('is false on any failure', async () => {
    server.use(mswHttp.get('/api/assistant/status', () => new HttpResponse(null, { status: 500 })));
    expect(await getAssistantEnabled()).toBe(false);
    server.use(mswHttp.get('/api/assistant/status', () => HttpResponse.error()));
    expect(await getAssistantEnabled()).toBe(false);
  });
});

describe('streamChat', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });

  it('posts with credentials and the XSRF header from the cookie, and yields events', async () => {
    document.cookie = 'XSRF-TOKEN=tok%20en';
    let header: string | null = null;
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    server.use(
      antiforgeryHandler(),
      mswHttp.post('/api/assistant/messages', ({ request }) => {
        header = request.headers.get('X-XSRF-TOKEN');
        return sse({ type: 'text_delta', text: 'Hello' }, { type: 'done' });
      }),
    );

    const events = await collect();

    expect(header).toBe('tok en');
    expect(fetchSpy.mock.calls.at(-1)?.[1]).toMatchObject({ credentials: 'include', method: 'POST' });
    expect(events.map((e) => e.type)).toEqual(['text_delta', 'done']);
  });

  it('sends no XSRF header when there is no cookie, and skips malformed and non-data lines', async () => {
    let header: string | null = 'unset';
    server.use(
      antiforgeryHandler(),
      mswHttp.post('/api/assistant/messages', ({ request }) => {
        header = request.headers.get('X-XSRF-TOKEN');
        return new HttpResponse(`: ping\n\ndata: not-json\n\ndata: ${JSON.stringify({ type: 'done' })}\n\n`);
      }),
    );

    expect((await collect()).map((e) => e.type)).toEqual(['done']);
    expect(header).toBeNull();
  });

  it('notifies the unauthorized handler on 401', async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    server.use(antiforgeryHandler(), mswHttp.post('/api/assistant/messages', () => new HttpResponse(null, { status: 401 })));

    const events = await collect();

    expect(handler).toHaveBeenCalledTimes(1);
    expect(events[0]).toMatchObject({ type: 'error' });
  });

  it('uses the rate-limit fallback on a bodyless 429 and the generic one on other failures', async () => {
    server.use(antiforgeryHandler(), mswHttp.post('/api/assistant/messages', () => new HttpResponse(null, { status: 429 })));
    expect((await collect())[0].error).toMatch(/Too many questions/);
    server.use(mswHttp.post('/api/assistant/messages', () => new HttpResponse(null, { status: 500 })));
    expect((await collect())[0].error).toMatch(/Try again/);
  });

  it('reports a network failure, and stays silent when aborted', async () => {
    server.use(antiforgeryHandler(), mswHttp.post('/api/assistant/messages', () => HttpResponse.error()));
    expect((await collect())[0].error).toMatch(/Check your connection/);

    const controller = new AbortController();
    controller.abort();
    const events: StreamEvent[] = [];
    for await (const e of streamChat({ messages: [] }, controller.signal)) events.push(e);
    expect(events).toEqual([]);
  });

  it('reports an error when the antiforgery cookie cannot be fetched', async () => {
    server.use(mswHttp.get('/api/antiforgery', () => HttpResponse.error()));
    expect((await collect())[0]).toMatchObject({ type: 'error' });
  });

  it('reports an error when the response has no body', async () => {
    server.use(antiforgeryHandler());
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({ ok: true, status: 200, body: null } as Response);
    expect((await collect())[0]).toMatchObject({ type: 'error' });
  });
});
