import { describe, expect, it } from 'vitest';
import { createDfrntCssVariablesResolver, createDfrntTheme, dfrntTheme } from './mantineTheme';
import { brandForCountry, contrastRatio, getMd3Scheme, md3Schemes } from './md3';

// Mirrors IntegrationManager's mantineTheme.test.ts and replaces the C# UrgentTenantThemeTests /
// TenantButtonTextColorTests: per-tenant brand, readable text on the brand fill, soft radii, static font.
describe('dfrnt Mantine theme', () => {
  it('uses the brand ramp as the primary colour in both modes', () => {
    expect(dfrntTheme.primaryColor).toBe('brand');
    expect(dfrntTheme.autoContrast).toBe(true);
    expect(dfrntTheme.primaryShade).toEqual({ light: 5, dark: 4 });
  });

  it('uses the static Plus Jakarta Sans face, not the variable one', () => {
    expect(dfrntTheme.fontFamily).toContain('Plus Jakarta Sans');
    expect(dfrntTheme.fontFamily).not.toMatch(/variable/i);
  });

  it('keeps controls softly rounded (not pill) but the Switch track fully round', () => {
    expect(dfrntTheme.defaultRadius).toBe('sm');
    expect(dfrntTheme.components?.Button?.defaultProps?.radius).toBe('sm');
    expect(dfrntTheme.components?.Switch?.defaultProps?.radius).toBe(9999);
  });

  it('gives menus a hairline border', () => {
    const dropdown = dfrntTheme.components?.Menu?.styles as unknown as { dropdown: { border: string } };
    expect(dropdown.dropdown.border).toContain('--dd-outline-variant');
  });

  it('shells in Ink Blue for DFRNT and amber for Urgent', () => {
    const dfrnt = createDfrntTheme('dfrnt').other?.shell?.appBar ?? '';
    const urgent = createDfrntTheme('urgent').other?.shell?.appBar ?? '';
    expect(dfrnt).not.toBe(urgent);
    expect(urgent.toLowerCase()).toBe(md3Schemes.urgent.light.primary.toLowerCase());
  });

  it('maps non-US countries to Urgent amber and US or unknown to DFRNT cyan', () => {
    expect(brandForCountry('NZ')).toBe('urgent');
    expect(brandForCountry('au')).toBe('urgent');
    expect(brandForCountry('US')).toBe('dfrnt');
    expect(brandForCountry('us')).toBe('dfrnt');
    expect(brandForCountry(null)).toBe('dfrnt');
    expect(brandForCountry(undefined)).toBe('dfrnt');
  });

  it.each(['dfrnt', 'urgent'] as const)('keeps Ink text readable (WCAG AA) on the %s brand fill', (brand) => {
    const s = getMd3Scheme(brand, 'light');
    expect(contrastRatio(s.onPrimary, s.primary)).toBeGreaterThanOrEqual(4.5);
  });

  it('resolves different surface variables per colour scheme and brand', () => {
    const urgent = createDfrntCssVariablesResolver('urgent')(createDfrntTheme('urgent') as never);
    const dfrnt = createDfrntCssVariablesResolver('dfrnt')(createDfrntTheme('dfrnt') as never);
    expect(urgent.light['--dd-surface']).toBeDefined();
    expect(urgent.light['--dd-surface']).not.toBe(urgent.dark['--dd-surface']);
    expect(urgent.dark['--dd-outline-variant']).toBeDefined();
    expect(dfrnt.dark['--dd-dialog-bar']).toBeDefined();
  });
});
