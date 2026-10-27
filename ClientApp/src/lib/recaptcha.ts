interface Grecaptcha {
  ready(callback: () => void): void;
  execute(siteKey: string, options: { action: string }): Promise<string>;
}

declare global {
  interface Window {
    grecaptcha?: Grecaptcha;
  }
}

const loading = new Map<string, Promise<Grecaptcha>>();

/** Loads Google's reCAPTCHA script at runtime for the given site key (once per key). */
export function loadRecaptcha(siteKey: string): Promise<Grecaptcha> {
  const existing = loading.get(siteKey);
  if (existing) return existing;

  const promise = new Promise<Grecaptcha>((resolve, reject) => {
    const script = document.createElement('script');
    script.src = `https://www.google.com/recaptcha/api.js?render=${encodeURIComponent(siteKey)}`;
    script.async = true;
    script.onload = () => (window.grecaptcha ? resolve(window.grecaptcha) : reject(new Error('reCAPTCHA unavailable')));
    script.onerror = () => {
      loading.delete(siteKey);
      script.remove();
      reject(new Error('reCAPTCHA failed to load'));
    };
    document.head.appendChild(script);
  });
  loading.set(siteKey, promise);
  return promise;
}

export async function getRecaptchaToken(siteKey: string, action: string): Promise<string> {
  const grecaptcha = await loadRecaptcha(siteKey);
  await new Promise<void>((resolve) => grecaptcha.ready(resolve));
  return grecaptcha.execute(siteKey, { action });
}
