// vite builds into ../wwwroot with emptyOutDir off, because wwwroot also holds images that are
// not part of the SPA. Without this, hashed chunks from earlier builds pile up in static/ and the
// service worker precaches them. Only the paths the SPA build owns are removed.
import { readdirSync, rmSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const wwwroot = resolve(dirname(fileURLToPath(import.meta.url)), '../../wwwroot');
const owned = ['static', 'icons', 'index.html', 'sw.js', 'registerSW.js', 'manifest.webmanifest', 'consent.css', 'favicon.svg'];

for (const name of owned) rmSync(resolve(wwwroot, name), { recursive: true, force: true });
for (const name of readdirSync(wwwroot)) {
  if (/^workbox-.*\.js$/.test(name)) rmSync(resolve(wwwroot, name), { force: true });
}
