// Build the favicon assets from wwwroot/images/DFRNT_Mark.png:
//   1. Write a transparent-background SVG embedding the mark as base64.
//   2. Rasterize the SVG to a multi-resolution favicon.ico (16/32/48).
// Run this whenever the source mark changes.

import { readFileSync, writeFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { Resvg } from '@resvg/resvg-js';
import pngToIco from 'png-to-ico';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '..');
const markPath = resolve(repo, 'wwwroot/images/DFRNT_Mark.png');
const svgPath = resolve(repo, 'wwwroot/images/favicon.svg');
const icoTargets = [
    resolve(repo, 'wwwroot/images/favicon.ico'),
    resolve(repo, 'wwwroot/favicon.ico'),
];

const markBase64 = readFileSync(markPath).toString('base64');
const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">` +
    `<image href="data:image/png;base64,${markBase64}" x="3" y="3" width="506" height="506"/>` +
    `</svg>\n`;
writeFileSync(svgPath, svg);
console.log(`wrote ${svgPath} (${svg.length} bytes)`);

const sizes = [16, 32, 48];
const pngs = sizes.map(size => {
    const resvg = new Resvg(svg, { fitTo: { mode: 'width', value: size } });
    return resvg.render().asPng();
});

const ico = await pngToIco(pngs);
for (const t of icoTargets) {
    writeFileSync(t, ico);
    console.log(`wrote ${t} (${ico.length} bytes)`);
}
