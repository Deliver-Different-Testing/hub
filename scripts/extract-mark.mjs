// Extract the DFRNT atomic/orbital mark from DFRNT_HorizLogo_RGB.png as a
// transparent square PNG. The source is on an opaque white background; we
// derive alpha from per-pixel whiteness so anti-aliased edges stay clean.

import sharp from 'sharp';
import { writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(here_url()));
function here_url() { return import.meta.url; }
const repo = resolve(here, '..');
const src = resolve(repo, 'wwwroot/images/DFRNT_HorizLogo_RGB.png');
const out = resolve(repo, 'wwwroot/images/DFRNT_Mark.png');

// Crop bounds discovered by scripts/inspect-logo.mjs
const CROP = { left: 183, top: 166, width: 506, height: 459 };

const raw = await sharp(src)
    .extract(CROP)
    .ensureAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });

const { data, info } = raw;
const { width, height, channels } = info;
const px = Buffer.from(data); // mutable copy

for (let i = 0; i < px.length; i += channels) {
    const r = px[i], g = px[i+1], b = px[i+2], a = px[i+3];
    // Whiteness = how close to (255,255,255). The min-channel is a good
    // approximation: a pixel with min channel = 200 means at least 200 of
    // white is mixed in. Derive new alpha from the non-white portion.
    const whiteness = Math.min(r, g, b);
    const newAlpha = Math.max(0, 255 - whiteness) * (a / 255);
    if (newAlpha === 0) {
        px[i] = px[i+1] = px[i+2] = px[i+3] = 0;
        continue;
    }
    // Un-premultiply: pre = bg*(1-α) + fg*α → fg = (pre - bg*(1-α)) / α
    // bg = 255 (white). Channel-wise.
    const invA = 255 / newAlpha;
    px[i]   = Math.max(0, Math.min(255, Math.round((r - (255 - newAlpha)) * invA)));
    px[i+1] = Math.max(0, Math.min(255, Math.round((g - (255 - newAlpha)) * invA)));
    px[i+2] = Math.max(0, Math.min(255, Math.round((b - (255 - newAlpha)) * invA)));
    px[i+3] = Math.round(newAlpha);
}

// Pad to square with transparent margins. 506x459 → 506x506.
const side = Math.max(width, height);
const padTop = Math.floor((side - height) / 2);
const padLeft = Math.floor((side - width) / 2);

await sharp(px, { raw: { width, height, channels } })
    .extend({
        top: padTop, bottom: side - height - padTop,
        left: padLeft, right: side - width - padLeft,
        background: { r: 0, g: 0, b: 0, alpha: 0 },
    })
    .png()
    .toFile(out);

console.log(`wrote ${out} (${side}x${side})`);
