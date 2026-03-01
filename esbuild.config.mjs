import * as esbuild from 'esbuild';

const watch = process.argv.includes('--watch');

const shared = {
  bundle: true,
  minify: true,
  sourcemap: false,
  outdir: 'wwwroot/dist',
  logLevel: 'info',
};

if (watch) {
  const ctx = await esbuild.context({
    ...shared,
    entryPoints: [
      'src/vendor.js',
      'src/vendor.css',
      'src/validation.js',
    ],
  });
  await ctx.watch();
  console.log('Watching for changes...');
} else {
  await esbuild.build({
    ...shared,
    entryPoints: [
      'src/vendor.js',
      'src/vendor.css',
      'src/validation.js',
    ],
  });
}
