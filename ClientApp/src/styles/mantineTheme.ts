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
import { getMd3Scheme, type Brand } from './md3';

// ---------------------------------------------------------------------------
// Brand ramps (0 lightest → 9 darkest). Light ends use the guideline tints
// (20% / 40%); dark ends are hand-tuned. Regenerate precisely with
// @mantine/colors-generator from the six seed hexes if needed.
// ---------------------------------------------------------------------------
const brand: MantineColorsTuple = [
  '#e7f8fe', '#d8f4fd', '#b1e9fb', '#82dcf8', '#5bd1f5',
  '#3bc7f4', '#1eb2e6', '#1590c0', '#0f6f96', '#0a4d69',
]; // Cyan — primary (DFRNT default)

const amber: MantineColorsTuple = [
  '#fef9e7', '#fcefc4', '#fae49d', '#f8d976', '#f6d058',
  '#f4c430', '#e5b52a', '#d4a324', '#c3911e', '#a87614',
]; // Amber — Urgent Couriers primary (mirrors md3.ts urgentPrimaryPalette)

/** Primary ramp per brand — selected by `createDfrntTheme(brand)`. */
const BRAND_TUPLES: Record<Brand, MantineColorsTuple> = { dfrnt: brand, urgent: amber };

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
const D = getMd3Scheme('dfrnt', 'dark');
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

/**
 * Shell (AppBar + sidebar) colours per brand. DFRNT is the fixed Ink-Blue navy in
 * both colour modes; Urgent Couriers is the full amber shell with dark-ink chrome.
 */
export function createShellColors(brand: Brand = 'dfrnt') {
  return brand === 'urgent'
    ? {
        appBar: amber[5],       // #f4c430 — amber bar
        bg: amber[6],           // #e5b52a — drawer panel, one tier down
        border: 'rgba(13, 12, 44, 0.12)',
        textPrimary: 'rgba(13, 12, 44, 0.95)', // dark ink on amber
        textSecondary: 'rgba(13, 12, 44, 0.60)',
        textMuted: 'rgba(13, 12, 44, 0.40)',
        hoverBg: 'rgba(13, 12, 44, 0.06)',
      }
    : {
        appBar: ink[9],           // #0d0c2c
        bg: ink[8],               // #141233 — drawer panel, one tier lighter than the bar
        border: 'rgba(255, 255, 255, 0.10)',
        textPrimary: 'rgba(255, 255, 255, 0.95)',
        textSecondary: 'rgba(255, 255, 255, 0.60)',
        textMuted: 'rgba(255, 255, 255, 0.38)',
        hoverBg: 'rgba(255, 255, 255, 0.08)',
      };
}

/** Fixed Ink-Blue shell — the DFRNT default (`createShellColors('dfrnt')`). */
export const sidebarColors = createShellColors('dfrnt');

/**
 * Overlays for content sitting on the shell/hero scrim, per brand. DFRNT uses white
 * overlays (on the Ink scrim); Urgent Couriers uses dark-ink overlays (on the amber scrim).
 */
export function createOnBrandScrim(brand: Brand = 'dfrnt') {
  return brand === 'urgent'
    ? {
        text: '#0d0c2c',
        bodyText: 'rgba(13,12,44,0.85)',
        subtleText: 'rgba(13,12,44,0.8)',
        mutedText: 'rgba(13,12,44,0.7)',
        fill: 'rgba(13,12,44,0.12)',
        fillStrong: 'rgba(13,12,44,0.18)',
        hoverFill: 'rgba(13,12,44,0.08)',
        faintHoverFill: 'rgba(13,12,44,0.06)',
        solidHoverFill: 'rgba(13,12,44,0.9)',
        border: 'rgba(13,12,44,0.35)',
      }
    : {
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
}

/** White-based overlays on the Ink scrim — the DFRNT default (`createOnBrandScrim('dfrnt')`). */
export const onBrandScrim = createOnBrandScrim('dfrnt');
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
  }
}

// ---------------------------------------------------------------------------
// The theme
// ---------------------------------------------------------------------------
/**
 * Build the DFRNT Mantine theme for a brand. `'dfrnt'` (default) is Cyan on the
 * Ink-Blue shell; `'urgent'` swaps the primary ramp + shell/scrim to the Urgent
 * Couriers amber. `autoContrast` flips button/nav text to Ink on the light amber
 * fills automatically, so no per-brand text overrides are needed. Pair with the
 * matching `createDfrntCssVariablesResolver(brand)`.
 */
export function createDfrntTheme(brand: Brand = 'dfrnt') {
  return createTheme({
  primaryColor: 'brand',
  primaryShade: { light: 5, dark: 4 },
  autoContrast: true,
  luminanceThreshold: 0.4,
  // FloatingIndicator and the control transitions read this; without it
  // Mantine animates regardless of the OS preference.
  respectReducedMotion: true,
  colors: { brand: BRAND_TUPLES[brand], ink, reflex, grape, green, orange, red, gray, dark },
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
    shell: createShellColors(brand),
    scrim: createOnBrandScrim(brand),
    code: codeBlockPalette,
    tokens,
  },
  });
}

/** The DFRNT-brand theme (Cyan / Ink-Blue) — the default for every tenant. */
export const dfrntTheme = createDfrntTheme('dfrnt');

/**
 * Bridges the DFRNT surface ladder (md3.ts, the single source of truth) into Mantine's
 * CSS variables per colour-scheme. Passed to `<MantineProvider cssVariablesResolver>`.
 *
 * `--mantine-color-body` sets the page/`AppShell.Main` background; the `--dd-surface-*`
 * custom vars back the Card/Paper/Menu/Modal surface defaults declared above, so a single
 * scheme flip repaints page → cards → menus with the intended tones.
 */
export function createDfrntCssVariablesResolver(brand: Brand = 'dfrnt'): CSSVariablesResolver {
  return () => {
  const light = getMd3Scheme(brand, 'light');
  const dk = getMd3Scheme(brand, 'dark');
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
    },
  };
  };
}

/** Surface-var resolver for the DFRNT default brand (`createDfrntCssVariablesResolver('dfrnt')`). */
export const dfrntCssVariablesResolver = createDfrntCssVariablesResolver('dfrnt');
