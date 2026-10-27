export const EMAIL_ERROR = 'Please enter a valid email address.';

const EMAIL_REGEX =
  /^(([^<>()[\]\\.,;:\s@"]+(\.([^<>()[\]\\.,;:\s@"]+))*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;

export function isValidEmail(email: string): boolean {
  return EMAIL_REGEX.test(email.toLowerCase());
}
