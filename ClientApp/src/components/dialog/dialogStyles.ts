/**
 * Shared style tokens for the DFRNT dialog design language (ported from
 * despatchweb's `components/dialogs/shared/mantine/styles.ts`, minus its opt-in
 * liquid-glass bars).
 *
 * The header/footer bars are one calm neutral surface with a keyline — colour is
 * NOT used to separate them. Meaning lives in the icon chip's accent alone, and
 * the `primary` accent follows the theme's primary colour, so a tenant brand
 * (cyan by default, amber for `createDfrntTheme('urgent')`) flows through with no
 * tenant check here. Everything is a CSS variable, so light/dark just works.
 *
 * Requires the `--dd-dialog-*` vars from `createDfrntCssVariablesResolver`.
 */
import type { CSSProperties } from 'react';
import { alpha } from '@mantine/core';

/** One width scale for every dialog, so sizes stop being ad-hoc. */
export const DIALOG_SIZES = { sm: 400, md: 520, lg: 720, xl: 960 } as const;
export type DialogSize = keyof typeof DIALOG_SIZES;

export type DialogVariant = 'primary' | 'secondary' | 'info' | 'success' | 'warning' | 'error';

/** The semantic hue behind each variant's icon chip — the only coloured part of a dialog header. */
export const dialogAccents: Record<DialogVariant, string> = {
  primary: 'var(--mantine-primary-color-filled)', // tenant brand: cyan, or amber for 'urgent'
  secondary: 'var(--mantine-color-grape-filled)',
  info: 'var(--mantine-color-cyan-filled)',
  success: 'var(--mantine-color-green-filled)',
  warning: 'var(--mantine-color-orange-filled)',
  error: 'var(--mantine-color-red-filled)',
};

/** Keyline between a bar and the body. */
export const dialogKeyline = '1px solid var(--dd-outline-variant)';

/**
 * Spread onto `<ThemeIcon {...dialogChipProps(variant)}>`: a 40px tile with a 15%
 * wash of the accent behind a glyph in the accent. Pass `accent` to override with
 * any CSS colour. Give the glyph its own size (ThemeIcon sizes the box, not the icon).
 */
export function dialogChipProps(variant: DialogVariant = 'primary', accent?: string, size = 40) {
  const color = accent ?? dialogAccents[variant];
  return {
    size,
    radius: 'md' as const,
    style: { '--ti-bg': alpha(color, 0.15), '--ti-color': color } as CSSProperties &
      Record<`--${string}`, string>,
  };
}

/** Elevation: a tight contact shadow plus a broad soft throw. Dialog-only. */
export const dialogModalShadow = '0 2px 4px rgba(0,0,0,.12), 0 24px 56px -8px rgba(0,0,0,.28)';

/**
 * A dialog is a three-band column — header / scrolling body / footer — and only the
 * body scrolls, so the scrollbar never runs past the header bar or across the corners.
 * `flex: '1 1 auto'` (not `flex: 1`) so a short dialog keeps its content height;
 * `minHeight: 0` is what lets the body shrink and scroll once Mantine's ~90dvh cap bites.
 */
export const dialogShellStyles = {
  content: {
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    backgroundColor: 'var(--dd-dialog-body)',
  },
  body: { padding: 0, display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0, overflow: 'hidden' },
} satisfies Record<'content' | 'body', CSSProperties>;

export const dialogScrollRegionStyle: CSSProperties = { flex: '1 1 auto', minHeight: 0, overflowY: 'auto' };

/** Props for a white "section" Paper inside dialog content — flat, keyline border, 10px corner. */
export const dialogSectionProps = {
  withBorder: true,
  radius: 'md',
  p: 'md',
  bg: 'var(--dd-dialog-section)',
} as const;
