export const APP_NAME = 'Deliver DFRNT';

const ANONYMOUS_PATHS = ['/account/login', '/account/forgotpassword', '/account/resetpassword'];

/** Routes that render without a signed-in user (api/me returns 401 there by design). */
export function isAnonymousPath(pathname: string): boolean {
  return ANONYMOUS_PATHS.includes(pathname.toLowerCase().replace(/\/+$/, ''));
}

export function loginUrl(returnUrl?: string): string {
  return returnUrl ? `/Account/Login?returnUrl=${encodeURIComponent(returnUrl)}` : '/Account/Login';
}
