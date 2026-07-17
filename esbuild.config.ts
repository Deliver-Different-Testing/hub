import * as esbuild from 'esbuild';
import type { BuildOptions } from 'esbuild';
import { lessLoader } from 'esbuild-plugin-less';

const watch = process.argv.includes('--watch');

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
