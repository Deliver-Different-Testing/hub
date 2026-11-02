import axios, { isAxiosError } from 'axios';

export const http = axios.create({
  baseURL: '/api',
  withCredentials: true,
  xsrfCookieName: 'XSRF-TOKEN',
  xsrfHeaderName: 'X-XSRF-TOKEN',
});

export const GENERIC_ERROR = "Couldn't reach the server. Check your connection and try again.";

export class ApiError extends Error {
  /** True when the message came from the server's { error } body rather than the generic fallback. */
  readonly fromServer: boolean;

  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message || GENERIC_ERROR);
    this.fromServer = Boolean(message);
  }
}

export function getApiErrorMessage(error: unknown, fallback = GENERIC_ERROR): string {
  return error instanceof ApiError && error.fromServer ? error.message : fallback;
}

// The antiforgery cookie is requested once, before the first mutation.
let antiforgery: Promise<unknown> | null = null;
export function resetAntiforgery(): void {
  antiforgery = null;
}

/** Resolves once the antiforgery cookie has been issued (requested at most once until reset). */
export async function ensureAntiforgery(): Promise<void> {
  antiforgery ??= http.get('antiforgery').catch((e: unknown) => {
    antiforgery = null;
    throw e;
  });
  await antiforgery;
}

/** The antiforgery token for requests that bypass axios (fetch streaming), read from the XSRF-TOKEN cookie. */
export function readXsrfToken(): string | undefined {
  const match = document.cookie.split('; ').find((c) => c.startsWith('XSRF-TOKEN='));
  return match ? decodeURIComponent(match.slice('XSRF-TOKEN='.length)) : undefined;
}

http.interceptors.request.use(async (config) => {
  const method = (config.method ?? 'get').toLowerCase();
  if (method !== 'get' && method !== 'head' && method !== 'options') await ensureAntiforgery();
  return config;
});

let onUnauthorized: (() => void) | null = null;
/** Registered inside the router so an expired session on an authed route navigates to the login page. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler;
}

/** Same 401 handling as the axios client, for calls made with fetch. */
export function notifyUnauthorized(): void {
  onUnauthorized?.();
}

http.interceptors.response.use(undefined, (error: unknown) => {
  if (isAxiosError<{ error?: string }>(error)) {
    const status = error.response?.status;
    if (status === 401) onUnauthorized?.();
    throw new ApiError(error.response?.data?.error ?? '', status);
  }
  throw error;
});
