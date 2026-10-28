/**
 * DFRNT Mantine theme — the single source of truth for the brand. Portable copy
 * from the IntegrationManager AdminPortal (src/styles/mantineTheme.ts). Only
 * dependency is `@mantine/core` and the sibling `./md3`.
 *
 * Built 1:1 from the Feb 2026 DFRNT brand guidelines:
 *   Ink Blue  #0d0c2c  — shell (AppBar + sidebar) in both modes, dark-mode page bg
 *   Cyan      #3bc7f4  — primary interactive (buttons, links, active nav, focus)
 *   Light Grey#f4f2f1  — light-mode page background
 * Secondary accents: Reflex Blue #2a4eff, Purple #824ae0, Green #13b964 (success),
 * Orange #fe811a (warning), Red #dc3246 (error).
 *
 * Typography: Plus Jakarta Sans for UI and display headings.
 *
 * Sep 2026 refinement (ported from despatchweb's `dfrntMantineTheme.ts`, the first
 * app to hit this at scale): the fully-rounded lozenge shape read as toy-like on
 * dense operational screens, so buttons/inputs/cards moved to a softly-rounded,
 * squared-off radius scale (Switch keeps its pill track — that's a toggle
 * affordance, not the dropped decorative shape) and the font dropped its
 * variable-weight face for a single static Plus Jakarta Sans. Consuming apps
 * should self-host the static weights they actually use via
 * `@fontsource/plus-jakarta-sans/<weight>.css` (or an equivalent static load, e.g.
 * a Google Fonts link) — not `@fontsource-variable/plus-jakarta-sans`.
 */
import {
  createTheme,
  type CSSVariablesResolver,
  type MantineColorsTuple,
} from '@mantine/core';
import { generateRamp } from './colorMath';
import { TEXT_MIN_CONTRAST_HIGH, type ThemeAppearance } from './appearance';
import { contrastRatio, getMd3Scheme, type Md3Mode } from './md3';

// ---------------------------------------------------------------------------
// Brand ramps (0 lightest → 9 darkest). Light ends use the guideline tints
// (20% / 40%); dark ends are hand-tuned. Regenerate precisely with
// @mantine/colors-generator from the six seed hexes if needed.
// ---------------------------------------------------------------------------
const brand: MantineColorsTuple = [
  '#e7f8fe', '#d8f4fd', '#b1e9fb', '#82dcf8', '#5bd1f5',
  '#3bc7f4', '#1eb2e6', '#1590c0', '#0f6f96', '#0a4d69',
]; // Cyan — the built-in DFRNT primary

const ink: MantineColorsTuple = [
  '#ecebf1', '#cfced5', '#a8a7b6', '#83829a', '#6e6d80',
  '#4f4e66', '#35334f', '#211f40', '#141233', '#0d0c2c',
]; // Ink Blue — shell / neutral-dark

const reflex: MantineColorsTuple = [
  '#eaeeff', '#d4dcff', '#aab8ff', '#7d92ff', '#5670ff',
  '#2a4eff', '#233fd6', '#1b31a8', '#132279', '#0b134a',
]; // #2a4eff — info / links

const grape: MantineColorsTuple = [
  '#f3edfc', '#e6dbf9', '#cdb7f3', '#b088ec', '#9865e5',
  '#824ae0', '#6c3bbe', '#552e95', '#3f226e', '#291546',
]; // #824ae0 — accent

const green: MantineColorsTuple = [
  '#e5f8ee', '#d0f1e0', '#a1e3c1', '#5fd199', '#2ec27a',
  '#13b964', '#0f9d55', '#0b7d44', '#085e33', '#053f22',
]; // #13b964 — success

const orange: MantineColorsTuple = [
  '#fff3e6', '#ffe6d1', '#ffcda3', '#ffab63', '#fe9333',
  '#fe811a', '#e06d10', '#b3560b', '#853f08', '#582905',
]; // #fe811a — warning

const red: MantineColorsTuple = [
  '#fdeaec', '#f8d6da', '#f1adb5', '#e97b88', '#e25062',
  '#dc3246', '#bb2a3c', '#93212f', '#6c1823', '#460f16',
]; // #dc3246 — error

// A warm-grey neutral ramp for the light surfaces (Light Grey #f4f2f1 family).
const gray: MantineColorsTuple = [
  '#ffffff', '#f8f7f7', '#f4f2f1', '#e7e5e4', '#d6d3d1',
  '#a8a29e', '#78716c', '#57534e', '#44403c', '#292524',
];

// Mantine's `dark` tuple drives every default surface/text role in dark mode
// (`--mantine-color-body` = dark[7], default component bg = dark[6], border = dark[4],
// text = dark[0], dimmed = dark[2]). We map it 1:1 onto the DFRNT charcoal ladder in
// md3.ts so raw Mantine roles resolve to our tones instead of Mantine's generic charcoal.
const D = getMd3Scheme('dark');
const dark: MantineColorsTuple = [
  D.onSurface,                // 0 text          #E6E1E9
  D.onSurfaceVariant,         // 1               #CAC4D0
  D.outline,                  // 2 dimmed        #939099
  '#6f6c75',                  // 3 (interpolated between outline and outline-variant)
  D.outlineVariant,           // 4 border        #56535c
  D.surfaceContainerHighest,  // 5 hover         #4c4952
  D.surfaceContainerHigh,     // 6 default bg     #413f47
  D.surface,                  // 7 body / page   #2c2a30
  D.surfaceDim,               // 8               #28262c
  D.surfaceContainerLowest,   // 9 deepest       #252429
];

// ---------------------------------------------------------------------------
// DFRNT design tokens — framework-free constants consumed across the app.
// ---------------------------------------------------------------------------

/** Shell (AppBar + sidebar): the fixed Ink-Blue navy in both colour modes, whatever primary a tenant sets. */
export const sidebarColors = {
  appBar: ink[9], // #0d0c2c
  bg: ink[8], // #141233 — drawer panel, one tier lighter than the bar
  border: 'rgba(255, 255, 255, 0.10)',
  textPrimary: 'rgba(255, 255, 255, 0.95)',
  textSecondary: 'rgba(255, 255, 255, 0.60)',
  textMuted: 'rgba(255, 255, 255, 0.38)',
  hoverBg: 'rgba(255, 255, 255, 0.08)',
};

/** White-based overlays for content sitting on the Ink shell/hero scrim. */
export const onBrandScrim = {
  text: '#fff',
  bodyText: 'rgba(255,255,255,0.85)',
  subtleText: 'rgba(255,255,255,0.8)',
  mutedText: 'rgba(255,255,255,0.7)',
  fill: 'rgba(255,255,255,0.15)',
  fillStrong: 'rgba(255,255,255,0.2)',
  hoverFill: 'rgba(255,255,255,0.1)',
  faintHoverFill: 'rgba(255,255,255,0.08)',
  solidHoverFill: 'rgba(255,255,255,0.9)',
  border: 'rgba(255,255,255,0.4)',
};
/** Theme-aware palette for code / terminal blocks (on Ink Blue). */
export const codeBlockPalette = {
  background: '#141233',
  backgroundError: '#2e1a1a',
  textSuccess: '#5fd199',
  textInfo: '#7d92ff',
  textError: '#f1adb5',
};

/**
 * Radius / duration / shadow tokens (softly-rounded corporate corner scale).
 * `xl` stays large deliberately — it's what makes `radius="xl"` clip to a full
 * circle on avatars/ThemeIcons app-wide; shrinking it would turn those circles
 * into rounded squares, which was never part of the "less rounded" change.
 */
export const tokens = {
  radius: { xs: 2, sm: 8, md: 10, lg: 14, xl: 28, full: 9999, tile: 2, card: 16 },
  duration: { instant: 100, fast: 150, normal: 200, slow: 350 },
  // Apple-style "spring" easing for restrained hover/press motion (e.g. a card's
  // hover scale) — additive, not part of the shared radius/shadow scale below.
  easing: { spring: 'cubic-bezier(0.32, 0.72, 0, 1)' },
  shadow: {
    sm: '0 1px 3px 0 rgba(0,0,0,.1), 0 1px 2px -1px rgba(0,0,0,.1)',
    md: '0 4px 6px -1px rgba(0,0,0,.1), 0 2px 4px -2px rgba(0,0,0,.1)',
    lg: '0 10px 15px -3px rgba(0,0,0,.1), 0 4px 6px -4px rgba(0,0,0,.1)',
    xl: '0 20px 25px -5px rgba(0,0,0,.1), 0 8px 10px -6px rgba(0,0,0,.1)',
  },
};

/** Back-compat brand token object (semantic hex, for the rare non-Mantine consumer). */
export const brandColors = {
  mainDark: ink[9],
  mainHighlight: brand[5],
  secondary: gray[6],
  surfaceWhite: '#ffffff',
  surfaceLight: gray[2],
  surfaceDark: ink[9],
  textPrimary: ink[9],
  textSecondary: 'rgba(13,12,44,0.64)',
  textMuted: 'rgba(13,12,44,0.44)',
  success: green[5],
  warning: orange[5],
  error: red[5],
  info: reflex[5],
};

// ---------------------------------------------------------------------------
// theme.other typing — access via `theme.other.shell` / `.scrim` / `.tokens` etc.
// ---------------------------------------------------------------------------
declare module '@mantine/core' {
  export interface MantineThemeOther {
    shell: typeof sidebarColors;
    scrim: typeof onBrandScrim;
    code: typeof codeBlockPalette;
    tokens: typeof tokens;
    appearance: ThemeAppearance;
  }
}

// ---------------------------------------------------------------------------
// The theme
// ---------------------------------------------------------------------------
const STANDARD_APPEARANCE: ThemeAppearance = { highContrast: false };

/**
 * Builds the theme for a tenant's resolved appearance. `brand` is DFRNT cyan unless the tenant sets a
 * primary, so every `color="brand"` / `--mantine-color-brand-*` consumer follows without edits; a custom
 * secondary replaces the `reflex` (links / info) ramp. The Ink shell and the semantic status ramps (red,
 * green, orange) are never customised. Pair with `dfrntCssVariablesResolver`.
 */
export function createDfrntTheme(appearance: ThemeAppearance = STANDARD_APPEARANCE) {
  const brandRamp = appearance.primary ? generateRamp(appearance.primary) : brand;
  const secondaryRamp = appearance.secondary ? generateRamp(appearance.secondary) : reflex;
  return createTheme({
  primaryColor: 'brand',
  primaryShade: { light: 5, dark: 4 },
  autoContrast: true,
  luminanceThreshold: 0.4,
  // FloatingIndicator and the control transitions read this; without it
  // Mantine animates regardless of the OS preference.
  respectReducedMotion: true,
  colors: { brand: brandRamp, ink, reflex: secondaryRamp, grape, green, orange, red, gray, dark },
  white: '#ffffff',
  black: ink[9],
  fontFamily: '"Plus Jakarta Sans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
  fontFamilyMonospace: 'ui-monospace, SFMono-Regular, Menlo, monospace',
  headings: {
    fontFamily: '"Plus Jakarta Sans", sans-serif',
    fontWeight: '600',
  },
  defaultRadius: 'sm',
  // `xl` stays large — see the `tokens.radius` comment above.
  radius: { xs: '2px', sm: '8px', md: '10px', lg: '14px', xl: '28px' },
  // Wires the app's own soft shadow scale (`tokens.shadow`) into Mantine's native
  // shadow system, so every `var(--mantine-shadow-*)` consumer app-wide gets the
  // same soft values instead of Mantine's un-customized defaults. `tokens.shadow`
  // has no `xs` tier, so it reuses `sm` — Mantine requires all five sizes.
  shadows: { xs: tokens.shadow.sm, ...tokens.shadow },
  components: {
    Button: { defaultProps: { radius: 'sm' } },
    ActionIcon: { defaultProps: { radius: 'sm' } },
    // Cards/paper sit one tonal tier above the page in both schemes, plus a soft
    // shadow for a little lift. Background applied as a class-based `styles.root`,
    // NOT a `bg` defaultProp, so a component's own inline `style={{ background }}`
    // still wins — hero cards paint a dark Ink scrim that way and must not be
    // overpainted white in light mode.
    // Cards/sheets get the larger `card` radius (16px) — Apple-style content
    // containers read as proportionally roomier than form controls (`sm`, 8px).
    Card: {
      defaultProps: { radius: tokens.radius.card, shadow: 'sm' },
      styles: { root: { backgroundColor: 'var(--dd-surface-container)' } },
    },
    Paper: {
      defaultProps: { radius: tokens.radius.card, shadow: 'sm' },
      styles: { root: { backgroundColor: 'var(--dd-surface-container)' } },
    },
    Modal: {
      defaultProps: {
        radius: tokens.radius.card,
        centered: true,
        shadow: 'md',
        // Native Mantine "glass scrim" behind the sheet — no custom backdrop-filter needed.
        overlayProps: { backgroundOpacity: 0.35, blur: 3 },
      },
      styles: { content: { backgroundColor: 'var(--dd-surface-container-high)' } },
    },
    TextInput: { defaultProps: { radius: 'sm' } },
    Textarea: { defaultProps: { radius: 'sm' } },
    Select: { defaultProps: { radius: 'sm' } },
    Autocomplete: { defaultProps: { radius: 'sm' } },
    Menu: {
      defaultProps: { radius: 'sm', shadow: 'md' },
      styles: {
        dropdown: {
          backgroundColor: 'var(--dd-surface-container-high)',
          border: '1px solid var(--dd-outline-variant)',
        },
      },
    },
    Tooltip: { defaultProps: { radius: 'sm', color: 'ink' } },
    Badge: { defaultProps: { radius: 'sm' } },
    Chip: { defaultProps: { radius: 'sm' } },
    SegmentedControl: { defaultProps: { radius: 'sm', withItemsBorders: false } },
    // Kept fully round: a pill toggle track is the universal switch affordance,
    // not the decorative lozenge shape the rest of the controls moved away from.
    Switch: { defaultProps: { radius: 9999 } },
    Radio: { defaultProps: { size: 'sm' } },
    // Thicker tab targets than Mantine's thin default — comfortable, easier to hit.
    Tabs: { styles: { tab: { paddingBlock: 14, fontWeight: 500 } } },
  },
  other: {
    shell: sidebarColors,
    scrim: onBrandScrim,
    code: codeBlockPalette,
    tokens,
    appearance,
  },
  });
}

/** The built-in DFRNT theme (Cyan / Ink-Blue) — what anonymous routes and tenants without branding get. */
export const dfrntTheme = createDfrntTheme();

/** Ramps whose `light`/`text`/`outline` roles carry meaning on the page (badges, links, alerts). */
const ROLE_RAMPS = ['brand', 'reflex', 'red', 'green', 'orange', 'grape', 'ink'] as const;

type ResolverTheme = Parameters<CSSVariablesResolver>[0];

function surfacesFor(mode: Md3Mode): string[] {
  const scheme = getMd3Scheme(mode);
  return mode === 'light'
    ? [scheme.surface, scheme.surfaceContainerLowest]
    : [scheme.surface, scheme.surfaceContainer, scheme.surfaceContainerHigh];
}

/**
 * The mildest shade of a ramp that reaches `min` against every page/card surface of the scheme (darker in
 * light mode, lighter in dark) so the hue survives where it can; the scheme's text colour when none does.
 */
function strongShade(ramp: readonly string[], mode: Md3Mode, min: number, fallback: string): string {
  const order = mode === 'light' ? [6, 7, 8, 9] : [3, 2, 1, 0];
  const surfaces = surfacesFor(mode);
  return order.map((i) => ramp[i]).find((hex) => surfaces.every((bg) => contrastRatio(hex, bg) >= min)) ?? fallback;
}

/**
 * High contrast strengthens what is already there rather than swapping palettes: dimmed text and
 * placeholders go full-strength, hairlines become stronger, and every coloured text role (light badges,
 * links, alerts) moves to a shade reaching 7:1.
 */
function highContrastVariables(theme: ResolverTheme, mode: Md3Mode): Record<string, string> {
  const scheme = getMd3Scheme(mode);
  // Secondary text shares the full-strength colour: the dark scheme's onSurfaceVariant stops short of 7:1.
  const text = mode === 'light' ? theme.colors.gray[8] : scheme.onSurface;
  const border = mode === 'light' ? theme.colors.gray[6] : scheme.outline;
  const vars: Record<string, string> = {
    '--mantine-color-dimmed': text,
    '--mantine-color-placeholder': text,
    '--mantine-color-default-border': border,
    '--dd-outline-variant': border,
    '--mantine-color-gray-light-color': text,
    '--mantine-color-gray-text': text,
    '--mantine-color-gray-outline': text,
  };
  for (const name of ROLE_RAMPS) {
    const strong = strongShade(theme.colors[name], mode, TEXT_MIN_CONTRAST_HIGH, text);
    vars[`--mantine-color-${name}-light-color`] = strong;
    vars[`--mantine-color-${name}-text`] = strong;
    vars[`--mantine-color-${name}-outline`] = strong;
  }
  vars['--mantine-color-anchor'] = vars['--mantine-color-brand-text'];
  return vars;
}

/**
 * Bridges the DFRNT surface ladder (md3.ts, the single source of truth) into Mantine's CSS variables
 * per colour-scheme. Passed to `<MantineProvider cssVariablesResolver>`.
 *
 * `--mantine-color-body` sets the page background; the `--dd-surface-*` custom vars back the
 * Card/Paper/Menu/Modal defaults above. A tenant's body-text colours and the high-contrast block read
 * the appearance carried on `theme.other`.
 */
export const dfrntCssVariablesResolver: CSSVariablesResolver = (theme) => {
  const light = getMd3Scheme('light');
  const dk = getMd3Scheme('dark');
  const { highContrast, textLight, textDark } = theme.other.appearance ?? STANDARD_APPEARANCE;
  return {
    variables: {},
    light: {
      '--mantine-color-body': light.surface, // #f4f2f1 page
      '--dd-surface': light.surface,
      '--dd-surface-container': light.surfaceContainerLowest, // #ffffff cards
      '--dd-surface-container-high': light.surfaceContainerLowest, // #ffffff menus/dialogs
      '--dd-outline-variant': light.outlineVariant, // #e7e5e4 keyline divider / hairline border
      // Dialog planes (assets/dialog/): bars sit a step off white, the body a step deeper.
      '--dd-dialog-section': 'var(--mantine-color-white)',
      '--dd-dialog-bar': 'var(--mantine-color-gray-1)',
      '--dd-dialog-body': 'var(--mantine-color-gray-2)',
      ...(highContrast ? highContrastVariables(theme, 'light') : {}),
      ...(textLight ? { '--mantine-color-text': textLight } : {}),
    },
    dark: {
      '--mantine-color-body': dk.surface, // #2c2a30 page
      '--dd-surface': dk.surface,
      '--dd-surface-container': dk.surfaceContainer, // #37353c cards
      '--dd-surface-container-high': dk.surfaceContainerHigh, // #413f47 menus/dialogs
      '--dd-outline-variant': dk.outlineVariant, // #56535c keyline divider / hairline border
      '--dd-dialog-section': dk.surfaceContainerHigh,
      '--dd-dialog-bar': dk.surfaceContainer,
      '--dd-dialog-body': dk.surfaceContainerLow,
      ...(highContrast ? highContrastVariables(theme, 'dark') : {}),
      ...(textDark ? { '--mantine-color-text': textDark } : {}),
    },
  };
};
