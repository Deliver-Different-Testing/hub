import { ensureAntiforgery, http, notifyUnauthorized, readXsrfToken } from './client';
import type {
  AiChatRequest,
  FuelSurchargeData,
  HomeData,
  LoginRequest,
  LogoResponse,
  Me,
  RedirectResponse,
  StreamEvent,
  TenantSetting,
} from './types';

/** 200 with the tenant's validated appearance, or 204 (empty body) when none is set. */
export const getTenantAppearance = (tenantId: number) =>
  http.get<unknown>(`tenant/${tenantId}/appearance`).then((r) => r.data);

export const getMe = () => http.get<Me>('me').then((r) => r.data);
export const getConfig = () => http.get<{ recaptchaSiteKey: string | null }>('config').then((r) => r.data);
export const getLogo = () => http.get<LogoResponse>('Logo').then((r) => r.data);
export const getHome = () => http.get<HomeData>('home').then((r) => r.data);
export const getFuelSurcharge = () => http.get<FuelSurchargeData>('fuel-surcharge').then((r) => r.data);
export const getSettings = () => http.get<TenantSetting[]>('me/settings').then((r) => r.data);

export const login = (body: LoginRequest) => http.post<RedirectResponse>('auth/login', body).then((r) => r.data);
export const logout = () => http.post<RedirectResponse>('auth/logout').then((r) => r.data);
export const forgotPassword = (body: { email: string; recaptchaToken: string }) =>
  http.post('auth/forgot-password', body).then((r) => r.data);
export const validateResetCode = (code: string) =>
  http.get<{ email: string }>(`auth/reset-password/${encodeURIComponent(code)}`).then((r) => r.data);
export const resetPassword = (body: { code: string; password: string; confirmPassword: string }) =>
  http.post<RedirectResponse>('auth/reset-password', body).then((r) => r.data);
export const updateCurrentTenant = (tenantId: number) =>
  http.put<RedirectResponse>('me/current-tenant', { tenantId }).then((r) => r.data);
export const generateApiKey = () => http.post<{ apiKey: string }>('me/api-key').then((r) => r.data);

/** False when the chat is switched off or the status call fails, so the widget stays hidden. */
export const getAssistantEnabled = () =>
  http
    .get<{ enabled?: boolean }>('assistant/status')
    .then((r) => r.data.enabled === true)
    .catch(() => false);

const CHAT_URL = '/api/assistant/messages';

/** The server's `{ error }` body when it failed before streaming; the rate limiter's own 429 has no body. */
async function readErrorReason(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { error?: unknown };
    return typeof body.error === 'string' && body.error ? body.error : null;
  } catch {
    return null;
  }
}

/** Posts one chat turn (fetch, since axios cannot stream) and yields the server-sent events until `done` or `error`. */
export async function* streamChat(request: AiChatRequest, signal: AbortSignal): AsyncGenerator<StreamEvent> {
  let response: Response;
  try {
    await ensureAntiforgery();
    const token = readXsrfToken();
    response = await fetch(CHAT_URL, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', ...(token ? { 'X-XSRF-TOKEN': token } : {}) },
      body: JSON.stringify(request),
      signal,
    });
  } catch {
    if (signal.aborted) return;
    yield { type: 'error', error: "Couldn't reach Auto-mate. Check your connection and try again." };
    return;
  }

  if (!response.ok) {
    if (response.status === 401) notifyUnauthorized();
    const fallback =
      response.status === 429 ? 'Too many questions in a row. Wait a moment, then try again.' : "Couldn't reach Auto-mate. Try again.";
    yield { type: 'error', error: (await readErrorReason(response)) ?? fallback };
    return;
  }

  const reader = response.body?.getReader();
  if (!reader) {
    yield { type: 'error', error: "Couldn't reach Auto-mate. Try again." };
    return;
  }

  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';

      for (const line of lines) {
        const trimmed = line.trim();
        if (!trimmed.startsWith('data: ')) continue;

        let event: StreamEvent;
        try {
          event = JSON.parse(trimmed.slice(6)) as StreamEvent;
        } catch {
          continue;
        }

        yield event;
        if (event.type === 'done' || event.type === 'error') return;
      }
    }
  } finally {
    reader.releaseLock();
  }
}
