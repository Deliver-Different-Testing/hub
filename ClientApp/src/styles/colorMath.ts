/**
 * Small, dependency-free colour helpers for tenant theme colours: HSL conversion, WCAG
 * luminance and a 10-shade Mantine ramp from one seed. Mirrors despatchweb's theme/colorMath.ts.
 */
import type { MantineColorsTuple } from '@mantine/core';

export interface Hsl {
  h: number; // 0-360
  s: number; // 0-1
  l: number; // 0-1
}

export function hexToHsl(hex: string): Hsl {
  const n = parseInt(hex.replace('#', ''), 16);
  const r = ((n >> 16) & 0xff) / 255;
  const g = ((n >> 8) & 0xff) / 255;
  const b = (n & 0xff) / 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return { h: 0, s: 0, l };
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h: number;
  if (max === r) h = (g - b) / d + (g < b ? 6 : 0);
  else if (max === g) h = (b - r) / d + 2;
  else h = (r - g) / d + 4;
  return { h: h * 60, s, l };
}

export function hslToHex({ h, s, l }: Hsl): string {
  const a = s * Math.min(l, 1 - l);
  const channel = (k: number): string => {
    const m = (k + h / 30) % 12;
    const value = l - a * Math.max(-1, Math.min(m - 3, 9 - m, 1));
    return Math.round(value * 255).toString(16).padStart(2, '0');
  };
  return `#${channel(0)}${channel(8)}${channel(4)}`;
}

/** WCAG relative luminance (0 black, 1 white). */
export function luminance(hex: string): number {
  const n = parseInt(hex.replace('#', ''), 16);
  const linear = (c: number): number => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear((n >> 16) & 0xff) + 0.7152 * linear((n >> 8) & 0xff) + 0.0722 * linear(n & 0xff);
}

const LIGHTEST = 0.97;
const DARKEST = 0.12;

/**
 * A 10-shade ramp (0 lightest, 9 darkest) with the seed itself at index 5, the shade the theme's
 * `primaryShade` uses in light mode, so the colour a tenant picks is the one they see.
 */
export function generateRamp(seed: string): MantineColorsTuple {
  const base = hexToHsl(seed);
  const top = Math.max(base.l, LIGHTEST);
  const bottom = Math.min(base.l, DARKEST);
  const shade = (i: number): string => {
    if (i === 5) return seed.toLowerCase();
    const l = i < 5 ? base.l + (top - base.l) * ((5 - i) / 5) : base.l - (base.l - bottom) * ((i - 5) / 4);
    return hslToHex({ ...base, l });
  };
  return Array.from({ length: 10 }, (_, i) => shade(i)) as unknown as MantineColorsTuple;
}
