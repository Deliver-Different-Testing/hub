import { describe, expect, it } from 'vitest';
import { generateRamp, hexToHsl, hslToHex, luminance } from './colorMath';

describe('colorMath', () => {
  it('round-trips hex through HSL', () => {
    for (const hex of ['#3bc7f4', '#f4c430', '#0d0c2c', '#ffffff', '#000000', '#808080']) {
      expect(hslToHex(hexToHsl(hex))).toBe(hex);
    }
  });

  it('measures relative luminance on the WCAG scale', () => {
    expect(luminance('#ffffff')).toBeCloseTo(1);
    expect(luminance('#000000')).toBeCloseTo(0);
  });

  it('generates ten shades with the seed at the primary index and lightness falling across the ramp', () => {
    const ramp = generateRamp('#2A4EFF');
    expect(ramp).toHaveLength(10);
    expect(ramp[5]).toBe('#2a4eff');
    const lightness = ramp.map((hex) => hexToHsl(hex).l);
    for (let i = 1; i < lightness.length; i++) {
      expect(lightness[i]).toBeLessThanOrEqual(lightness[i - 1] + 0.005);
    }
  });

  it('still spans light to dark for a pale seed', () => {
    const ramp = generateRamp('#f4c430');
    expect(luminance(ramp[0])).toBeGreaterThan(luminance(ramp[5]));
    expect(luminance(ramp[9])).toBeLessThan(0.1);
  });
});
