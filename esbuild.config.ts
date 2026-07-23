import * as esbuild from 'esbuild';
import type { BuildOptions } from 'esbuild';
import { lessLoader } from 'esbuild-plugin-less';
import * as fs from 'node:fs';
import * as path from 'node:path';

const watch = process.argv.includes('--watch');

// DFRNT icon system (brand book "Iconography - UI"): Lucide for generic UI
// chrome, Tabler for transport/logistics. Icons are inlined server-side by the
// <dfrnt-icon> TagHelper, which reads raw SVGs from wwwroot/dist/icons/. This
// step scans the views for every <dfrnt-icon set="…" name="…"> in use and
// copies just those SVGs out of node_modules — node_modules is not deployed,
// wwwroot/dist is. Devs add an icon simply by using the tag; a rebuild syncs it.
const ICON_SOURCES: Record<string, string> = {
  lucide: 'node_modules/lucide-static/icons',
  tabler: 'node_modules/@tabler/icons/icons/outline',
};

function collectCshtml(dir: string, acc: string[] = []): string[] {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) collectCshtml(full, acc);
    else if (entry.name.endsWith('.cshtml')) acc.push(full);
  }
  return acc;
}

function syncIcons(): void {
  const used = new Map<string, Set<string>>(); // set -> names
  const tagRe = /<dfrnt-icon\b([^>]*?)\/?>/g;
  const attr = (tag: string, name: string) =>
    new RegExp(`${name}\\s*=\\s*"([^"]*)"`).exec(tag)?.[1];

  for (const file of collectCshtml('Views')) {
    const html = fs.readFileSync(file, 'utf8');
    for (const [, tag] of html.matchAll(tagRe)) {
      const name = attr(tag, 'name');
      if (!name) continue;
      const set = attr(tag, 'set') ?? 'lucide';
      (used.get(set) ?? used.set(set, new Set()).get(set)!).add(name);
    }
  }

  let copied = 0;
  for (const [set, names] of used) {
    const src = ICON_SOURCES[set];
    if (!src) {
      console.warn(`[icons] unknown icon set "${set}" — expected one of ${Object.keys(ICON_SOURCES).join(', ')}`);
      continue;
    }
    const destDir = path.join('wwwroot/dist/icons', set);
    fs.mkdirSync(destDir, { recursive: true });
    for (const name of names) {
      const from = path.join(src, `${name}.svg`);
      if (!fs.existsSync(from)) {
        console.warn(`[icons] "${set}/${name}" not found at ${from}`);
        continue;
      }
      fs.copyFileSync(from, path.join(destDir, `${name}.svg`));
      copied++;
    }
  }
  console.log(`[icons] synced ${copied} icon(s)`);
}

syncIcons();

const shared: BuildOptions = {
  bundle: true,
  minify: true,
  sourcemap: watch,
  target: ['es2020'],
  outdir: 'wwwroot/dist',
  logLevel: 'error',
};

const configs: BuildOptions[] = [
  {
    ...shared,
    entryPoints: [
      'src/vendor.js',
      'src/vendor.css',
    ],
  },
  {
    ...shared,
    entryPoints: ['src/validation.js'],
    external: ['jquery'],
    banner: { js: '(function(){var require=function(m){if(m==="jquery")return window.jQuery;throw new Error("unexpected require: "+m)};' },
    footer: { js: '})();' },
  },
  {
    ...shared,
    entryPoints: [
      'src/layout.ts',
      'src/login.ts',
      'src/forgot-password.ts',
      'src/settings.ts',
      'src/home.ts',
      'src/fuel-surcharge.ts',
      'src/material.ts',
    ],
  },
  {
    // reset-password lazy-loads zxcvbn-ts (the English dictionary is heavy) via
    // dynamic import(). Code splitting requires ESM output, so this entry is
    // built separately and the view loads it with <script type="module">. The
    // dictionary lands in its own content-hashed chunk fetched on demand.
    ...shared,
    entryPoints: ['src/reset-password.ts'],
    format: 'esm',
    splitting: true,
    chunkNames: '[name]-[hash]',
  },
  {
    ...shared,
    entryPoints: [
      'wwwroot/css/site.less',
      'wwwroot/css/login.less',
      'wwwroot/css/material-theme.less',
    ],
    plugins: [lessLoader()],
  },
];

if (watch) {
  const contexts = await Promise.all(configs.map(c => esbuild.context(c)));
  await Promise.all(contexts.map(c => c.watch()));
  console.log('Watching for changes...');
} else {
  await Promise.all(configs.map(c => esbuild.build(c)));
}
