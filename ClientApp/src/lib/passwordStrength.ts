import type { ZxcvbnFactory } from '@zxcvbn-ts/core';

type Zxcvbn = InstanceType<typeof ZxcvbnFactory>;

// Composition rule mirrors the backend [StrongPassword] attribute: the server rejects anything
// that fails it, so the client MUST keep enforcing it too.
const PASSWORD_REGEX = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$/;
export const PASSWORD_INVALID_MESSAGE =
  'Password must be at least 8 characters long, contain at least one uppercase letter, one lowercase letter, one number, and one special character.';
export const CONFIRM_MISMATCH_MESSAGE = "Passwords don't match";

// zxcvbn gate layered on top of the composition rule: a rule-compliant password such as
// "Password1!" can still be trivially guessable. 0-4; 3 = "good".
export const MIN_STRENGTH_SCORE = 3;
export const WEAK_PASSWORD_MESSAGE = 'This password is too easy to guess. Try a longer or less predictable one.';
export const STRENGTH_LABELS = ['Very weak', 'Weak', 'Fair', 'Good', 'Strong'] as const;

export function isStrongPassword(pwd: string): boolean {
  return PASSWORD_REGEX.test(pwd);
}

// zxcvbn-ts + its English dictionary are large, so they are pulled in on demand via dynamic
// import() (a separate chunk) and memoised. Preloaded on first focus of the password field.
let factoryPromise: Promise<Zxcvbn> | null = null;
export function loadZxcvbn(): Promise<Zxcvbn> {
  factoryPromise ??= (async () => {
    const [{ ZxcvbnFactory }, common, en] = await Promise.all([
      import('@zxcvbn-ts/core'),
      import('@zxcvbn-ts/language-common'),
      import('@zxcvbn-ts/language-en'),
    ]);
    return new ZxcvbnFactory({
      dictionary: { ...common.dictionary, ...en.dictionary },
      graphs: common.adjacencyGraphs,
      translations: en.translations,
    });
  })();
  return factoryPromise;
}

export interface StrengthResult {
  score: number;
  feedback: string;
}

export async function scorePassword(value: string): Promise<StrengthResult> {
  const result = (await loadZxcvbn()).check(value);
  return { score: result.score, feedback: result.feedback.warning || result.feedback.suggestions[0] || '' };
}
