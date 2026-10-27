/**
 * DFRNT surface ladder + semantic scheme builder — framework-free, portable.
 *
 * This is the single source of truth for DFRNT's colour roles and the dark
 * charcoal surface ladder (shared verbatim with the Hub app's LESS
 * `.dd-dark-scheme()` ramp — keep the two in sync). Copied from the
 * IntegrationManager AdminPortal (src/styles/md3.ts). No dependencies.
 *
 * `getMd3Scheme(brand, mode)` returns the scheme for the given brand: `'dfrnt'`
 * (Cyan — the default for every tenant) or `'urgent'` (the Urgent Couriers amber).
 * Map a tenant to its brand with `brandForTenant()`.
 */

export type Md3Mode = 'light' | 'dark';
export type Brand = 'dfrnt' | 'urgent';

/** Brand seeds — DFRNT Cyan is the default; Urgent Couriers renders in amber. */
export const SEEDS: Record<Brand, string> = {
  dfrnt: '#3bc7f4', // DFRNT brand cyan
  urgent: '#f4c430', // Urgent Couriers amber (pre-DFRNT NZ brand)
};

// Primary palette — DFRNT brand cyan (#3bc7f4).
export const dfrntPrimaryPalette = {
  50: '#e7f8fe',
  100: '#d8f4fd',
  200: '#b1e9fb',
  300: '#82dcf8',
  400: '#5bd1f5',
  500: '#3bc7f4', // Main color
  600: '#1eb2e6',
  700: '#1590c0',
  800: '#0f6f96',
  900: '#0a4d69',
  A100: '#b1e9fb',
  A200: '#82dcf8',
  A400: '#3bc7f4',
  A700: '#1590c0',
};

// Urgent Couriers primary — warm amber gold. Mirrors despatchweb's pre-DFRNT NZ
// brand palette (wwwroot/app/react/theme/palettes.ts `urgentPrimaryPalette`,
// @nz-primary #f4c430 / @nz-primary-dark #d4a324). Dark ink text on every shade.
export const urgentPrimaryPalette = {
  50: '#fef9e7',
  100: '#fcefc4',
  200: '#fae49d',
  300: '#f8d976',
  400: '#f6d058',
  500: '#f4c430',
  600: '#e5b52a',
  700: '#d4a324',
  800: '#c3911e',
  900: '#a87614',
  A100: '#fff8e1',
  A200: '#ffecb3',
  A400: '#ffd54f',
  A700: '#ffc107',
};

// Accent palette — warm grays (neutral / secondary / surfaces).
export const accentPalette = {
  50: '#fafaf9', // Warm white
  100: '#f5f5f4', // Very light warm gray
  200: '#e7e5e4', // Light warm gray
  300: '#d6d3d1', // Medium-light warm gray
  400: '#a8a29e', // Medium warm gray
  500: '#78716c', // Balanced warm gray — MAIN COLOR
  600: '#57534e', // Dark warm gray — TOOLBAR COLOR
  700: '#44403c', // Darker warm gray
  800: '#292524', // Very dark warm gray
  900: '#1c1917', // Deepest warm gray
};

const RAMPS: Record<Brand, typeof dfrntPrimaryPalette> = {
  dfrnt: dfrntPrimaryPalette,
  urgent: urgentPrimaryPalette,
};

export interface Md3Scheme {
  primary: string;
  primaryLight: string;
  primaryDark: string;
  onPrimary: string;
  primaryContainer: string;
  onPrimaryContainer: string;
  secondary: string;
  onSecondary: string;
  secondaryContainer: string;
  onSecondaryContainer: string;
  surface: string;
  onSurface: string;
  onSurfaceVariant: string;
  surfaceContainerLowest: string;
  surfaceContainerLow: string;
  surfaceContainer: string;
  surfaceContainerHigh: string;
  surfaceContainerHighest: string;
  surfaceDim: string;
  surfaceBright: string;
  outline: string;
  outlineVariant: string;
  inverseSurface: string;
  inverseOnSurface: string;
}

const N = accentPalette;

// Cool soft-charcoal dark tiers — the canonical DFRNT dark surface ladder, shared
// with the Hub app (its `.dd-dark-scheme()` --dd-surface* ramp was ported from these
// values). A gentle warm-neutral mid-charcoal lifted well clear of near-black. The
// container ladder stays monotonic (lowest → highest gets progressively lighter),
// preserving tonal elevation.
const darkSurface = {
  dim: '#28262c', // distinct dim floor (darkest)
  containerLowest: '#252429',
  base: '#2c2a30', // surface / page background
  containerLow: '#312f36',
  container: '#37353c', // elevated surface — cards / paper in dark
  containerHigh: '#413f47', // menus / popovers in dark
  containerHighest: '#4c4952',
  bright: '#56535c', // Hub --dd-surface-variant (lightest neutral fill)
};

function buildScheme(brand: Brand, dark: boolean): Md3Scheme {
  const P = RAMPS[brand];
  return dark
    ? {
        primary: P[300],
        primaryLight: P[200],
        primaryDark: P[400],
        onPrimary: P[900],
        primaryContainer: P[900],
        onPrimaryContainer: P[100],
        secondary: N[300],
        onSecondary: N[900],
        secondaryContainer: N[700],
        onSecondaryContainer: N[100],
        surface: darkSurface.base,
        onSurface: '#E6E1E9', // cool near-white, matches Hub's white-on-dark text
        onSurfaceVariant: '#CAC4D0', // cool light grey (matches Hub dark neutrals)
        surfaceContainerLowest: darkSurface.containerLowest,
        surfaceContainerLow: darkSurface.containerLow,
        surfaceContainer: darkSurface.container,
        surfaceContainerHigh: darkSurface.containerHigh,
        surfaceContainerHighest: darkSurface.containerHighest,
        surfaceDim: darkSurface.dim,
        surfaceBright: darkSurface.bright,
        outline: '#939099', // cool grey outline (Hub --dd-outline)
        outlineVariant: '#56535c', // Hub --dd-outline-variant
        inverseSurface: '#E6E1E9',
        inverseOnSurface: '#322F35',
      }
    : {
        // Light — despatchweb exact.
        primary: P[500],
        primaryLight: P[300],
        primaryDark: P[700],
        onPrimary: '#0d0c2c', // dark Ink text on light cyan (WCAG)
        primaryContainer: P[50],
        onPrimaryContainer: P[900],
        secondary: N[500],
        onSecondary: '#ffffff',
        secondaryContainer: N[100],
        onSecondaryContainer: N[900],
        surface: '#f4f2f1', // Light Grey page background
        onSurface: '#0d0c2c',
        onSurfaceVariant: '#6e6d80', // brand Ink-Blue 60% — secondary text/icons (p8)
        surfaceContainerLowest: '#ffffff',
        surfaceContainerLow: '#f8f7f7',
        surfaceContainer: N[100],
        surfaceContainerHigh: N[200],
        surfaceContainerHighest: N[300],
        surfaceDim: N[200],
        surfaceBright: '#ffffff',
        outline: N[400],
        outlineVariant: N[200],
        inverseSurface: N[700],
        inverseOnSurface: '#ffffff',
      };
}

export const md3Schemes: Record<Brand, Record<Md3Mode, Md3Scheme>> = {
  dfrnt: { light: buildScheme('dfrnt', false), dark: buildScheme('dfrnt', true) },
  urgent: { light: buildScheme('urgent', false), dark: buildScheme('urgent', true) },
};

export function getMd3Scheme(brand: Brand = 'dfrnt', mode: Md3Mode): Md3Scheme {
  return md3Schemes[brand][mode];
}

/**
 * Map a tenant identity to its brand. `'urgent'` renders the Urgent Couriers amber;
 * every other tenant gets the default DFRNT cyan. Integrators adapt the predicate to
 * their own tenant source (id / code / name) — this default matches on the name.
 */
export function brandForTenant(tenant?: string | null): Brand {
  return /urgent/i.test(tenant ?? '') ? 'urgent' : 'dfrnt';
}

/**
 * despatchweb's rule: any tenant whose country is known and isn't the US is amber.
 * `undefined`/`null` (context not loaded yet) stays DFRNT cyan so first paint doesn't flash.
 */
export function brandForCountry(countryCode?: string | null): Brand {
  return countryCode && countryCode.toUpperCase() !== 'US' ? 'urgent' : 'dfrnt';
}

/** Relative-luminance WCAG contrast ratio between two hex colors. */
export function contrastRatio(fgHex: string, bgHex: string): number {
  const toLinear = (c: number): number => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  const lum = (hex: string): number => {
    const n = parseInt(hex.replace('#', ''), 16);
    const r = toLinear((n >> 16) & 0xff);
    const g = toLinear((n >> 8) & 0xff);
    const b = toLinear(n & 0xff);
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const l1 = lum(fgHex);
  const l2 = lum(bgHex);
  const [hi, lo] = l1 > l2 ? [l1, l2] : [l2, l1];
  return (hi + 0.05) / (lo + 0.05);
}
