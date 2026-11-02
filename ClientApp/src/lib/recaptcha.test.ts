import { afterEach, describe, expect, it, vi } from 'vitest';
import { getRecaptchaToken, loadRecaptcha } from './recaptcha';

const scripts = () => Array.from(document.head.querySelectorAll<HTMLScriptElement>('script[src*="recaptcha"]'));

afterEach(() => {
  scripts().forEach((s) => s.remove());
  delete window.grecaptcha;
});

describe('recaptcha loader', () => {
  it('injects the script once per site key and resolves when grecaptcha is available', async () => {
    const grecaptcha = { ready: (cb: () => void) => cb(), execute: vi.fn().mockResolvedValue('tok') };
    const first = loadRecaptcha('key-a');
    const second = loadRecaptcha('key-a');
    expect(first).toBe(second);
    expect(scripts()).toHaveLength(1);
    expect(scripts()[0].src).toBe('https://www.google.com/recaptcha/api.js?render=key-a');

    window.grecaptcha = grecaptcha;
    scripts()[0].onload?.(new Event('load'));
    await expect(first).resolves.toBe(grecaptcha);
  });

  it('rejects when the script loads but grecaptcha is missing', async () => {
    const p = loadRecaptcha('key-b');
    scripts()[0].onload?.(new Event('load'));
    await expect(p).rejects.toThrow('reCAPTCHA unavailable');
  });

  it('rejects, removes the script and allows a retry when the script fails to load', async () => {
    const p = loadRecaptcha('key-c');
    scripts()[0].onerror?.(new Event('error'));
    await expect(p).rejects.toThrow('failed to load');
    expect(scripts()).toHaveLength(0);
    void loadRecaptcha('key-c').catch(() => undefined);
    expect(scripts()).toHaveLength(1);
  });

  it('executes with the given action once ready', async () => {
    const execute = vi.fn().mockResolvedValue('the-token');
    window.grecaptcha = { ready: (cb) => cb(), execute };
    const p = getRecaptchaToken('key-d', 'forgot_password');
    scripts().find((s) => s.src.includes('key-d'))?.onload?.(new Event('load'));
    await expect(p).resolves.toBe('the-token');
    expect(execute).toHaveBeenCalledWith('key-d', { action: 'forgot_password' });
  });
});
