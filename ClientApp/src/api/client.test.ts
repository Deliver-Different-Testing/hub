import { http as mswHttp, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { ApiError, getApiErrorMessage, GENERIC_ERROR, http, setUnauthorizedHandler } from './client';

describe('api client', () => {
  it('is configured for same-origin cookie auth with the antiforgery header', () => {
    expect(http.defaults.baseURL).toBe('/api');
    expect(http.defaults.withCredentials).toBe(true);
    expect(http.defaults.xsrfCookieName).toBe('XSRF-TOKEN');
    expect(http.defaults.xsrfHeaderName).toBe('X-XSRF-TOKEN');
  });

  it('requests the antiforgery cookie once, before the first mutation only', async () => {
    const antiforgery = vi.fn();
    server.use(
      mswHttp.get('/api/antiforgery', () => {
        antiforgery();
        return new HttpResponse(null, { status: 204 });
      }),
      mswHttp.get('/api/thing', () => HttpResponse.json({})),
      mswHttp.post('/api/thing', () => HttpResponse.json({})),
    );

    await http.get('thing');
    expect(antiforgery).not.toHaveBeenCalled();
    await http.post('thing');
    await http.post('thing');
    expect(antiforgery).toHaveBeenCalledTimes(1);
  });

  it('retries the antiforgery request on the next mutation when it failed', async () => {
    let calls = 0;
    server.use(
      mswHttp.get('/api/antiforgery', () => (++calls === 1 ? HttpResponse.error() : new HttpResponse(null, { status: 204 }))),
      mswHttp.post('/api/thing', () => HttpResponse.json({ ok: true })),
    );

    await expect(http.post('thing')).rejects.toBeDefined();
    await expect(http.post('thing')).resolves.toMatchObject({ data: { ok: true } });
    expect(calls).toBe(2);
  });

  it('maps an { error } body to an ApiError carrying the status', async () => {
    server.use(
      mswHttp.get('/api/antiforgery', () => new HttpResponse(null, { status: 204 })),
      mswHttp.post('/api/bad', () => HttpResponse.json({ error: 'Invalid login attempt.' }, { status: 400 })),
    );

    const error = await http.post('bad').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ message: 'Invalid login attempt.', status: 400, fromServer: true });
    expect(getApiErrorMessage(error, 'fallback')).toBe('Invalid login attempt.');
  });

  it('falls back to the generic message when there is no server message', async () => {
    server.use(mswHttp.get('/api/down', () => new HttpResponse(null, { status: 500 })));

    const error = await http.get('down').catch((e: unknown) => e);
    expect(error).toMatchObject({ message: GENERIC_ERROR, status: 500, fromServer: false });
    expect(getApiErrorMessage(error, 'custom fallback')).toBe('custom fallback');
    expect(getApiErrorMessage(new Error('x'))).toBe(GENERIC_ERROR);
  });

  it('rethrows non-HTTP errors untouched', async () => {
    server.use(mswHttp.get('/api/net', () => HttpResponse.error()));
    const error = await http.get('net').catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBeUndefined();
  });

  it('invokes the unauthorized handler on a 401 and still rejects', async () => {
    const handler = vi.fn();
    setUnauthorizedHandler(handler);
    server.use(mswHttp.get('/api/secret', () => HttpResponse.json({}, { status: 401 })));

    await expect(http.get('secret')).rejects.toMatchObject({ status: 401 });
    expect(handler).toHaveBeenCalledTimes(1);
  });
});
