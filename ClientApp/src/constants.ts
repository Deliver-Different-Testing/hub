export const APP_NAME = 'Deliver DFRNT';

// The AI-app unavailable page is reached before sign-in, from the app's own login window.
const ANONYMOUS_PATHS = ['/account/login', '/account/forgotpassword', '/account/resetpassword', '/agent-access/unavailable'];

/** Routes that render without a signed-in user (api/me returns 401 there by design). */
export function isAnonymousPath(pathname: string): boolean {
  return ANONYMOUS_PATHS.includes(pathname.toLowerCase().replace(/\/+$/, ''));
}

export function loginUrl(returnUrl?: string): string {
  return returnUrl ? `/Account/Login?returnUrl=${encodeURIComponent(returnUrl)}` : '/Account/Login';
}
