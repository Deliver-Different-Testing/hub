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

http.interceptors.request.use(async (config) => {
  const method = (config.method ?? 'get').toLowerCase();
  if (method !== 'get' && method !== 'head' && method !== 'options') {
    antiforgery ??= http.get('antiforgery').catch((e: unknown) => {
      antiforgery = null;
      throw e;
    });
    await antiforgery;
  }
  return config;
});

let onUnauthorized: (() => void) | null = null;
/** Registered inside the router so an expired session on an authed route navigates to the login page. */
export function setUnauthorizedHandler(handler: (() => void) | null): void {
  onUnauthorized = handler;
}

http.interceptors.response.use(undefined, (error: unknown) => {
  if (isAxiosError<{ error?: string }>(error)) {
    const status = error.response?.status;
    if (status === 401) onUnauthorized?.();
    throw new ApiError(error.response?.data?.error ?? '', status);
  }
  throw error;
});
