const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const SHORT_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Dates arrive as tenant wall-clock ISO strings; read the calendar parts and never convert zones. */
function parts(iso: string): { y: number; m: number; d: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  return match ? { y: Number(match[1]), m: Number(match[2]) - 1, d: Number(match[3]) } : null;
}

/** "Tuesday, 27 October" */
export function formatHeaderDate(iso: string): string {
  const p = parts(iso);
  if (!p) return '';
  const weekday = new Date(Date.UTC(p.y, p.m, p.d)).getUTCDay();
  return `${WEEKDAYS[weekday]}, ${p.d} ${MONTHS[p.m]}`;
}

/** "27 Oct 2026", or "Open" when there is no date. */
export function formatShortDate(iso: string | null | undefined): string {
  const p = iso ? parts(iso) : null;
  return p ? `${p.d} ${SHORT_MONTHS[p.m]} ${p.y}` : 'Open';
}

export const formatPercent = (value: number | null | undefined) =>
  value == null ? '—' : `${(value * 100).toFixed(2)}%`;

export const formatPump = (value: number | null | undefined) => (value == null ? '—' : value.toFixed(4));

/** "jane.doe@x.com" -> "Jane Doe" (fallback when the account has no display name). */
export function displayNameFromEmail(email: string): string {
  const name = email
    .split('@')[0]
    .split(/[ .\-_]+/)
    .filter(Boolean)
    .map((p) => p[0].toUpperCase() + p.slice(1).toLowerCase())
    .join(' ');
  return name || email;
}
