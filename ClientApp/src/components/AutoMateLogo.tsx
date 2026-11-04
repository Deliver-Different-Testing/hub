import autoMateUrl from '../assets/auto-mate.png';

// The brand Auto-mate mark. It is a gradient with opaque white inside its orbits, so it
// only reads on a surface: never put it on a primary fill.
export function AutoMateLogo({ size = 24 }: { size?: number }) {
  return (
    <img
      src={autoMateUrl}
      alt=""
      aria-hidden
      width={size}
      height={size}
      style={{ display: 'block', flexShrink: 0, objectFit: 'contain' }}
    />
  );
}
