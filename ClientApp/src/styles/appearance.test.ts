import { describe, expect, it } from 'vitest';
import { parseAppearanceSettings, resolveHighContrast, toThemeAppearance } from './appearance';

describe('parseAppearanceSettings', () => {
  it('keeps valid fields from a JSON string or an object and drops the rest', () => {
    expect(parseAppearanceSettings('{"contrast":"high","primary":"#2A4EFF","textLight":"red","bogus":1}')).toEqual({
      contrast: 'high',
      primary: '#2a4eff',
    });
    expect(parseAppearanceSettings({ secondary: '#13b964', contrast: 'loud' })).toEqual({ secondary: '#13b964' });
  });

  it('returns null for empty, malformed or field-less input (including the 204 empty body)', () => {
    expect(parseAppearanceSettings(null)).toBeNull();
    expect(parseAppearanceSettings('')).toBeNull();
    expect(parseAppearanceSettings('{not json')).toBeNull();
    expect(parseAppearanceSettings('{"textDark":"#abc"}')).toBeNull();
    expect(parseAppearanceSettings('"high"')).toBeNull();
  });
});

describe('resolveHighContrast', () => {
  it('follows the OS preference for auto (the default) and forces it either way otherwise', () => {
    expect(resolveHighContrast(undefined, true)).toBe(true);
    expect(resolveHighContrast('auto', false)).toBe(false);
    expect(resolveHighContrast('high', false)).toBe(true);
    expect(resolveHighContrast('standard', true)).toBe(false);
  });
});

describe('toThemeAppearance', () => {
  it('resolves contrast into a flag and keeps the colours', () => {
    expect(toThemeAppearance({ contrast: 'auto', primary: '#111111' }, true)).toEqual({ highContrast: true, primary: '#111111' });
    expect(toThemeAppearance(null, false)).toEqual({ highContrast: false });
  });
});
