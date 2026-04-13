import * as esbuild from 'esbuild';
import type { BuildOptions } from 'esbuild';

const watch = process.argv.includes('--watch');

const shared: BuildOptions = {
  bundle: true,
  minify: true,
  sourcemap: false,
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
      'src/reset-password.ts',
      'src/settings.ts',
    ],
  },
];

if (watch) {
  const contexts = await Promise.all(configs.map(c => esbuild.context(c)));
  await Promise.all(contexts.map(c => c.watch()));
  console.log('Watching for changes...');
} else {
  await Promise.all(configs.map(c => esbuild.build(c)));
}
