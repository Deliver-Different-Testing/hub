import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  MIN_STRENGTH_SCORE,
  STRENGTH_LABELS,
  isStrongPassword,
  loadZxcvbn,
  scorePassword,
} from './passwordStrength';

// Ports Hub.Tests/Views/ResetPasswordStrengthTests.cs.
describe('passwordStrength', () => {
  const source = readFileSync(join(__dirname, 'passwordStrength.ts'), 'utf8');

  it('lazy-loads zxcvbn through dynamic import() and never statically', () => {
    expect(source).toMatch(/import\('@zxcvbn-ts\/core'\)/);
    expect(source).toMatch(/import\('@zxcvbn-ts\/language-common'\)/);
    expect(source).toMatch(/import\('@zxcvbn-ts\/language-en'\)/);
    expect(source).toContain('new ZxcvbnFactory');
    expect(source).not.toMatch(/import\s*\{\s*ZxcvbnFactory/);
  });

  it('depends on the zxcvbn-ts packages', () => {
    const pkg = readFileSync(join(__dirname, '../../package.json'), 'utf8');
    for (const name of ['@zxcvbn-ts/core', '@zxcvbn-ts/language-common', '@zxcvbn-ts/language-en']) {
      expect(pkg).toContain(name);
    }
  });

  it('gates on the strength score', () => {
    expect(MIN_STRENGTH_SCORE).toBe(3);
    expect(STRENGTH_LABELS).toEqual(['Very weak', 'Weak', 'Fair', 'Good', 'Strong']);
  });

  it('keeps the composition rule that mirrors the backend', () => {
    expect(isStrongPassword('Abcdef1!')).toBe(true);
    expect(isStrongPassword('abcdef1!')).toBe(false); // no upper
    expect(isStrongPassword('ABCDEF1!')).toBe(false); // no lower
    expect(isStrongPassword('Abcdefg!')).toBe(false); // no digit
    expect(isStrongPassword('Abcdefg1')).toBe(false); // no special
    expect(isStrongPassword('Ab1!')).toBe(false); // too short
  });

  it('memoises the estimator and scores guessable passwords below the gate', async () => {
    expect(loadZxcvbn()).toBe(loadZxcvbn());
    const weak = await scorePassword('Password1!');
    expect(weak.score).toBeLessThan(MIN_STRENGTH_SCORE);
    const strong = await scorePassword('t7#Qm!vZp9$Lw2xR');
    expect(strong.score).toBeGreaterThanOrEqual(MIN_STRENGTH_SCORE);
    expect(typeof strong.feedback).toBe('string');
  });
});
