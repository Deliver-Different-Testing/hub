import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// The service worker and build output are only produced by `vite build`; these checks pin the config that shapes them.
const config = readFileSync(join(__dirname, '../../vite.config.ts'), 'utf8');

describe('vite PWA / build configuration', () => {
  it('builds into the ASP.NET wwwroot without wiping it, with hashed assets under /static', () => {
    expect(config).toContain("outDir: '../wwwroot'");
    expect(config).toContain('emptyOutDir: false');
    expect(config).toContain("assetsDir: 'static'");
  });

  it('prompts for updates instead of auto-updating', () => {
    expect(config).toContain("registerType: 'prompt'");
  });

  it('declares an installable standalone manifest', () => {
    expect(config).toContain("name: 'DFRNT Hub'");
    expect(config).toContain("short_name: 'Hub'");
    expect(config).toContain("display: 'standalone'");
    expect(config).toContain("start_url: '/'");
    expect(config).toMatch(/icon-192\.png/);
    expect(config).toMatch(/icon-512\.png/);
    expect(config).toMatch(/purpose: 'maskable'/);
  });

  it('takes manifest colours from md3.ts rather than literals', () => {
    expect(config).toContain('getMd3Scheme');
    expect(config).not.toMatch(/(theme_color|background_color): '#/);
  });

  it('falls back to index.html for navigations but never for server-owned routes', () => {
    expect(config).toContain("navigateFallback: '/index.html'");
    for (const route of ['api', 'connect', 'health', 'diagnostics', 'Account\\/AcceptTenantSwitchToken', 'Account\\/CreditCard', 'images']) {
      expect(config).toContain(`/^\\/${route}/`);
    }
  });

  it('never caches /api responses and precaches only the Vite output', () => {
    expect(config).toMatch(/urlPattern: \/\^\\\/api.*handler: 'NetworkOnly'/);
    expect(config).toContain('globPatterns');
    expect(config).not.toMatch(/globPatterns:[^\]]*images/);
  });

  it('emits the OAuth consent stylesheet unhashed at the output root', () => {
    expect(config).toContain('src/styles/consent/consent.css');
    expect(config).toContain("'consent.css'");
    const entry = readFileSync(join(__dirname, '../styles/consent/consent.css'), 'utf8');
    expect(entry).toContain('bootstrap.min.css');
    expect(entry).toContain('dfrnt-bootstrap.css');
  });

  it('proxies the server-owned routes to Kestrel in dev', () => {
    for (const route of ['/api', '/connect', '/Account/AcceptTenantSwitchToken', '/Account/CreditCard', '/images', '/health']) {
      expect(config).toContain(`'${route}': backend`);
    }
    expect(config).toContain('hub.local.deliverdifferent.com');
  });
});
