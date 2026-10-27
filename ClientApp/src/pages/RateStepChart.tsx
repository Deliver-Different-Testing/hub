import { formatPercent, formatShortDate } from '../lib/format';
import classes from './fuelSurcharge.module.css';

export interface RatePoint {
  start: string;
  rate: number;
}

const W = 600;
const H = 72;
const PAD = 8;

/** Calendar day number from a wall-clock ISO string; positions only, no zone maths. */
const day = (iso: string) => Date.UTC(Number(iso.slice(0, 4)), Number(iso.slice(5, 7)) - 1, Number(iso.slice(8, 10))) / 86_400_000;

/**
 * The standard rate over time as a step line: a surcharge holds until the next one starts, so steps
 * are honest where a sloped line would invent in-between rates. The table below is the data view.
 */
export default function RateStepChart({ points, today }: { points: RatePoint[]; today: string }) {
  if (points.length < 2) return null;

  const x0 = day(points[0].start);
  const x1 = Math.max(day(today), day(points[points.length - 1].start) + 1);
  const rates = points.map((p) => p.rate);
  const lo = Math.min(...rates);
  const hi = Math.max(...rates);
  const x = (iso: string) => PAD + ((day(iso) - x0) / (x1 - x0)) * (W - 2 * PAD);
  const xEnd = W - PAD;
  const y = (rate: number) => (hi === lo ? H / 2 : H - PAD - ((rate - lo) / (hi - lo)) * (H - 2 * PAD));

  const steps = points.map((p, i) => ({ ...p, from: x(p.start), to: i + 1 < points.length ? x(points[i + 1].start) : xEnd }));
  const d = steps.map((s, i) => `${i === 0 ? `M${s.from} ${y(s.rate)}` : `V${y(s.rate)}`} H${s.to}`).join(' ');
  const last = steps[steps.length - 1];
  const label = `Standard rate from ${formatPercent(points[0].rate)} to ${formatPercent(last.rate)} over ${points.length} rates`;

  return (
    <svg className={classes.chart} viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label}>
      <line className={classes.chartBaseline} x1={PAD} x2={xEnd} y1={H - 1} y2={H - 1} />
      <path className={classes.chartLine} d={d} />
      {steps.map((s) => (
        <rect key={s.start} className={classes.chartHit} x={s.from} y={0} width={Math.max(s.to - s.from, 4)} height={H}>
          <title>{`${formatPercent(s.rate)} from ${formatShortDate(s.start)}`}</title>
        </rect>
      ))}
      <circle className={classes.chartDot} cx={xEnd} cy={y(last.rate)} r={5} />
    </svg>
  );
}
