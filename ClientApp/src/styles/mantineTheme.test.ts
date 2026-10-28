import { describe, expect, it } from 'vitest';
import { createDfrntTheme, dfrntCssVariablesResolver, dfrntTheme, sidebarColors } from './mantineTheme';
import { contrastRatio, getMd3Scheme } from './md3';

// Mirrors IntegrationManager's mantineTheme.test.ts and despatchweb's appearance specs: one built-in brand,
// readable text on the brand fill, soft radii, static font, and the tenant appearance layered on top.
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

  it('shells in Ink Blue and keeps Ink readable (WCAG AA) on the cyan brand fill', () => {
    expect(sidebarColors.appBar.toLowerCase()).toBe('#0d0c2c');
    expect(dfrntTheme.other?.shell?.appBar).toBe(sidebarColors.appBar);
    const s = getMd3Scheme('light');
    expect(contrastRatio(s.onPrimary, s.primary)).toBeGreaterThanOrEqual(4.5);
  });

  it('resolves different surface variables per colour scheme', () => {
    const vars = dfrntCssVariablesResolver(dfrntTheme as never);
    expect(vars.light['--dd-surface']).toBeDefined();
    expect(vars.light['--dd-surface']).not.toBe(vars.dark['--dd-surface']);
    expect(vars.dark['--dd-outline-variant']).toBeDefined();
    expect(vars.dark['--dd-dialog-bar']).toBeDefined();
  });
});

describe('createDfrntTheme tenant appearance', () => {
  const light = getMd3Scheme('light');
  const dk = getMd3Scheme('dark');

  it('generates the brand and secondary ramps from the chosen colours, keeping the status ramps fixed', () => {
    const theme = createDfrntTheme({ highContrast: false, primary: '#13b964', secondary: '#dc3246' });
    expect(theme.colors?.brand?.[5]).toBe('#13b964');
    expect(theme.colors?.reflex?.[5]).toBe('#dc3246');
    expect(theme.colors?.red).toEqual(dfrntTheme.colors?.red);
    expect(theme.colors?.green).toEqual(dfrntTheme.colors?.green);
    expect(theme.colors?.orange).toEqual(dfrntTheme.colors?.orange);
  });

  it('leaves the Ink shell alone when a custom primary is chosen', () => {
    const theme = createDfrntTheme({ highContrast: false, primary: '#f4c430' });
    expect(theme.other?.shell?.appBar).toBe(sidebarColors.appBar);
    expect(theme.other?.scrim?.text).toBe('#fff');
  });

  it('sets the body text colour per scheme only when one is chosen', () => {
    const standard = dfrntCssVariablesResolver(dfrntTheme as never);
    expect(standard.light['--mantine-color-text']).toBeUndefined();
    const custom = dfrntCssVariablesResolver(createDfrntTheme({ highContrast: false, textLight: '#222222', textDark: '#eeeeee' }) as never);
    expect(custom.light['--mantine-color-text']).toBe('#222222');
    expect(custom.dark['--mantine-color-text']).toBe('#eeeeee');
  });

  it('in high contrast, lifts dimmed text, borders and every coloured text role to 7:1 in both schemes', () => {
    for (const primary of [undefined, '#f4c430']) {
      const vars = dfrntCssVariablesResolver(createDfrntTheme({ highContrast: true, primary }) as never);
      for (const [mode, surfaces] of [
        ['light', [light.surface, light.surfaceContainerLowest]],
        ['dark', [dk.surface, dk.surfaceContainer, dk.surfaceContainerHigh]],
      ] as const) {
        const scheme = vars[mode];
        for (const role of ['dimmed', 'brand-light-color', 'brand-text', 'red-light-color', 'reflex-text', 'gray-light-color']) {
          for (const bg of surfaces) {
            expect(contrastRatio(scheme[`--mantine-color-${role}`], bg)).toBeGreaterThanOrEqual(7);
          }
        }
        expect(scheme['--dd-outline-variant']).toBe(scheme['--mantine-color-default-border']);
      }
    }
  });

  it('leaves the contrast variables alone in standard contrast', () => {
    const vars = dfrntCssVariablesResolver(createDfrntTheme({ highContrast: false }) as never);
    expect(vars.light['--mantine-color-dimmed']).toBeUndefined();
    expect(vars.dark['--mantine-color-brand-text']).toBeUndefined();
  });
});
