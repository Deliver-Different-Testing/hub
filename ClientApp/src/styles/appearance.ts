/**
 * Tenant appearance: contrast level plus optional primary, secondary and body-text colours, served by
 * `GET api/tenant/{id}/appearance` (the tenant's `tblSetting.AppearanceDefaultsJson`). There is no per-user
 * override in the Hub. The server already validates; this re-validates so a bad payload can never reach the theme.
 */

export type ContrastPreference = 'auto' | 'standard' | 'high';

export interface AppearanceSettings {
  contrast?: ContrastPreference;
  primary?: string;
  secondary?: string;
  textLight?: string;
  textDark?: string;
}

export type AppearanceColorField = 'primary' | 'secondary' | 'textLight' | 'textDark';

export const APPEARANCE_COLOR_FIELDS: readonly AppearanceColorField[] = ['primary', 'secondary', 'textLight', 'textDark'];

/** What the theme needs once contrast is resolved against the OS preference. */
export interface ThemeAppearance {
  highContrast: boolean;
  primary?: string;
  secondary?: string;
  textLight?: string;
  textDark?: string;
}

/** AAA bar for text in high contrast. */
export const TEXT_MIN_CONTRAST_HIGH = 7;

const HEX_COLOR = /^#[0-9a-f]{6}$/i;
const CONTRAST_VALUES: readonly ContrastPreference[] = ['auto', 'standard', 'high'];

export function isHexColor(value: unknown): value is string {
  return typeof value === 'string' && HEX_COLOR.test(value);
}

/** Valid fields of a stored settings blob (JSON string or object), or null when none are. */
export function parseAppearanceSettings(raw: unknown): AppearanceSettings | null {
  let value = raw;
  if (typeof raw === 'string') {
    if (!raw) return null;
    try {
      value = JSON.parse(raw);
    } catch {
      return null;
    }
  }
  if (!value || typeof value !== 'object') return null;
  const source = value as Record<string, unknown>;
  const settings: AppearanceSettings = {};
  if (CONTRAST_VALUES.includes(source.contrast as ContrastPreference)) {
    settings.contrast = source.contrast as ContrastPreference;
  }
  for (const field of APPEARANCE_COLOR_FIELDS) {
    const color = source[field];
    if (isHexColor(color)) settings[field] = color.toLowerCase();
  }
  return Object.keys(settings).length > 0 ? settings : null;
}

export function resolveHighContrast(contrast: ContrastPreference | undefined, systemPrefersMore: boolean): boolean {
  return contrast === 'high' || ((contrast ?? 'auto') === 'auto' && systemPrefersMore);
}

export function toThemeAppearance(settings: AppearanceSettings | null, systemPrefersMore: boolean): ThemeAppearance {
  const { contrast, ...colors } = settings ?? {};
  return { ...colors, highContrast: resolveHighContrast(contrast, systemPrefersMore) };
}
