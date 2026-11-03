import { useId } from 'react';
import autoMateUrl from '../assets/auto-mate.png';

// The PNG is a gradient mark with opaque white inside its orbits, so it only reads on a
// surface. `mono` draws the same mark in `currentColor` for filled controls, where the
// control's autoContrast foreground (dark on light primaries, white on dark) colours it.
export function AutoMateLogo({ size = 24, variant = 'color' }: { size?: number; variant?: 'color' | 'mono' }) {
  if (variant === 'mono') return <AutoMateMonoMark size={size} />;
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

const ORBIT_ANGLES = [0, 60, 120];
const VIEW_BOX = { x: -28.5, y: -28.5, width: 57, height: 57 };

function AutoMateMonoMark({ size }: { size: number }) {
  const id = useId().replace(/[^a-zA-Z0-9_-]/g, '');
  const orbitsMask = `${id}-orbits`;
  const faceMask = `${id}-face`;
  return (
    <svg
      aria-hidden
      width={size}
      height={size}
      viewBox={`${VIEW_BOX.x} ${VIEW_BOX.y} ${VIEW_BOX.width} ${VIEW_BOX.height}`}
      style={{ display: 'block', flexShrink: 0 }}
    >
      <defs>
        {/* Orbits stop short of the nucleus so the face keeps a clear ring around it. */}
        <mask id={orbitsMask} maskUnits="userSpaceOnUse" {...VIEW_BOX}>
          <rect {...VIEW_BOX} fill="white" />
          <circle r="13.5" fill="black" />
        </mask>
        <mask id={faceMask} maskUnits="userSpaceOnUse" {...VIEW_BOX}>
          <circle r="11" fill="white" />
          <circle cx="-4.2" cy="-2.2" r="2.5" fill="black" />
          <circle cx="4.2" cy="-2.2" r="2.5" fill="black" />
          <path d="M -4 3.8 Q 0 7.2 4 3.8" fill="none" stroke="black" strokeWidth="2" strokeLinecap="round" />
        </mask>
      </defs>
      <g mask={`url(#${orbitsMask})`} fill="none" stroke="currentColor" strokeWidth="3.8">
        {ORBIT_ANGLES.map((angle) => (
          <ellipse key={angle} rx="25.6" ry="8.6" transform={`rotate(${angle})`} />
        ))}
      </g>
      <circle r="11" fill="currentColor" mask={`url(#${faceMask})`} />
    </svg>
  );
}
