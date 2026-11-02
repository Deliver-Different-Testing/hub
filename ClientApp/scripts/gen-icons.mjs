// Generates the PWA icons from the DFRNT mark. Run: npm run gen:icons
import { mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';
import { getMd3Scheme } from '../src/styles/md3.ts';

const here = dirname(fileURLToPath(import.meta.url));
const mark = resolve(here, '../../wwwroot/images/DFRNT_Mark.png');
const out = resolve(here, '../public/icons');
mkdirSync(out, { recursive: true });

const ink = getMd3Scheme('dfrnt', 'light').onPrimary; // Ink Blue

async function any(size) {
  await sharp(mark).resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png().toFile(resolve(out, `icon-${size}.png`));
}

// Maskable: full-bleed Ink background, mark inside the 80% safe zone.
async function maskable(size) {
  const inner = Math.round(size * 0.6);
  const glyph = await sharp(mark).resize(inner, inner, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } }).png().toBuffer();
  await sharp({ create: { width: size, height: size, channels: 4, background: ink } })
    .composite([{ input: glyph, gravity: 'centre' }]).png().toFile(resolve(out, `icon-maskable-${size}.png`));
}

await any(192);
await any(512);
await maskable(512);
console.log('icons written to', out);
