import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';
import { getMd3Scheme } from './src/styles/md3.ts';

// Manifest colours come from md3.ts: Ink Blue is the shell/theme colour, the light page surface is the background.
const light = getMd3Scheme('light');
const THEME_COLOR = light.onPrimary; // Ink Blue
const BACKGROUND_COLOR = light.surface;

const isTest = process.env.VITEST === 'true';
const backend = process.env.HUB_BACKEND ?? 'http://localhost:5000';

export default defineConfig({
  plugins: [
    react(),
    !isTest &&
      VitePWA({
        registerType: 'prompt',
        injectRegister: false,
        manifest: {
          name: 'DFRNT Hub',
          short_name: 'Hub',
          description: 'DFRNT app hub',
          display: 'standalone',
          start_url: '/',
          scope: '/',
          theme_color: THEME_COLOR,
          background_color: BACKGROUND_COLOR,
          icons: [
            { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
            { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
            { src: '/icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
          ],
        },
        workbox: {
          // Only the Vite output is precached, never the rest of wwwroot (images, css, ...).
          globPatterns: ['index.html', 'static/**/*.{js,css}', 'static/**/*latin-*.woff2', 'icons/*.png', 'favicon.svg'],
          navigateFallback: '/index.html',
          navigateFallbackDenylist: [
            /^\/api/,
            /^\/connect/,
            /^\/health/,
            /^\/diagnostics/,
            /^\/Account\/AcceptTenantSwitchToken/,
            /^\/Account\/CreditCard/,
            /^\/images/,
          ],
          globIgnores: ['static/zxcvbn*.js', 'static/lottie*.js'], // lazy, single-page chunks (reset-password dictionary, login animation): not worth precaching
          cleanupOutdatedCaches: true,
          runtimeCaching: [{ urlPattern: /^\/api(\/|$)/, handler: 'NetworkOnly' }],
        },
      }),
  ],
  base: '/',
  resolve: isTest
    ? { alias: { 'virtual:pwa-register/react': fileURLToPath(new URL('./src/test/pwaRegisterStub.ts', import.meta.url)) } }
    : undefined,
  server: {
    port: 5173,
    host: true,
    allowedHosts: ['hub.local.deliverdifferent.com'],
    proxy: {
      '/api': backend,
      '/connect': backend,
      '/Account/AcceptTenantSwitchToken': backend,
      '/Account/CreditCard': backend,
      '/images': backend,
      '/health': backend,
    },
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: false,
    assetsDir: 'static',
    rollupOptions: {
      input: {
        main: fileURLToPath(new URL('./index.html', import.meta.url)),
        consent: fileURLToPath(new URL('./src/styles/consent/consent.css', import.meta.url)),
      },
      output: {
        assetFileNames: (asset) =>
          asset.names?.some((n) => n === 'consent.css') ? 'consent.css' : 'static/[name]-[hash][extname]',
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined;
          if (/node_modules\/(react|react-dom|react-router|react-router-dom)\//.test(id)) return 'vendor-react';
          if (id.includes('@mantine')) return 'vendor-mantine';
          if (id.includes('@tanstack') || id.includes('axios')) return 'vendor-query';
          if (id.includes('@zxcvbn-ts')) return 'zxcvbn'; // lazy: loaded on the reset-password page only
          return undefined;
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    testTimeout: 30000,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/main.tsx', 'src/test/**', 'src/**/*.test.{ts,tsx}', 'src/vite-env.d.ts', 'src/styles/**', 'src/components/dialog/**', 'src/lib/navigate.ts'],
      reporter: ['text', 'html'],
      thresholds: { statements: 93, branches: 88, functions: 93, lines: 96 },
    },
  },
});
