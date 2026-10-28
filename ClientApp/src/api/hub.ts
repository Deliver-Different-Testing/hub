import { http } from './client';
import type {
  FuelSurchargeData,
  HomeData,
  LoginRequest,
  LogoResponse,
  Me,
  RedirectResponse,
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
