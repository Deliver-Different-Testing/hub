/**
 * MD3-allowed hero atmospheric background: a subtle radial highlight over a
 * solid brand color. Use ONLY on hero banners / greeting cards (the documented
 * carve-out) — never as a general surface fill. Regular surfaces stay flat.
 */
export function heroBackground(color: string): string {
  return `radial-gradient(ellipse at 25% 15%, rgba(255,255,255,0.12) 0%, transparent 60%), ${color}`;
}
