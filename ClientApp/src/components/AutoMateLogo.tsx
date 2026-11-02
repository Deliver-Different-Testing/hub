import autoMateUrl from '../assets/auto-mate.png';

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
