import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

// Replaces the C# BrandThemeComplianceTests / TenantButtonTextColorTests / UrgentTenantThemeTests source scans:
// colours come from theme tokens, never from a literal hex outside src/styles.
const SRC = join(__dirname, '..');
const HEX = /#[0-9a-fA-F]{3,8}\b/g;

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    return statSync(full).isDirectory() ? walk(full) : [full];
  });
}

const scanned = walk(SRC).filter((file) => {
  const rel = relative(SRC, file).split(sep).join('/');
  return /\.(ts|tsx|css)$/.test(rel) && !rel.startsWith('styles/') && !rel.startsWith('test/') && !/\.test\.(ts|tsx)$/.test(rel);
});

describe('brand compliance', () => {
  it('scans the application sources', () => {
    expect(scanned.length).toBeGreaterThan(20);
  });

  it('has no literal hex colours outside src/styles', () => {
    const offenders = scanned.flatMap((file) =>
      (readFileSync(file, 'utf8').match(HEX) ?? []).map((hex) => `${relative(SRC, file)}: ${hex}`),
    );
    expect(offenders).toEqual([]);
  });

  it('detects a hex literal (guards the scanner itself)', () => {
    expect('color: #0d0c2c'.match(HEX)).toHaveLength(1);
    expect('background: var(--dd-surface)'.match(HEX)).toBeNull();
  });
});
