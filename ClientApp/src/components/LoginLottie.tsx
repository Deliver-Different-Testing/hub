import { useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useColorMode } from '../hooks/useColorMode';

export const LOTTIE_LIGHT = '/images/Auto-mate-Ready%20state%20(2).json';
export const LOTTIE_DARK = '/images/Auto-mate-Ready%20state%20(2)_dark.json';
const SIZE = 200;

// Just the slice of the Lottie format the blend touches.
interface LottieShape {
  ty: string;
  it?: LottieShape[];
  s?: { k: number[] };
  c?: { a: number; k: number[] };
}
interface LottieLayer {
  ty: number;
  nm?: string;
  shapes?: LottieShape[];
}
interface LottieData {
  w?: number;
  h?: number;
  layers: LottieLayer[];
  assets?: { layers?: LottieLayer[] }[];
}

const INK = [0.0706, 0.0667, 0.1686];
const WHITE = [1, 1, 1];

const flatten = (shapes: LottieShape[] = []): LottieShape[] => shapes.flatMap((s) => [s, ...flatten(s.it)]);
const isInk = (k: number[]) => INK.every((v, i) => Math.abs(k[i] - v) < 0.001);

/**
 * Both files paint a full-canvas rectangle behind the mark (white, or grey in the dark file), which shows as a
 * square on the card. Dropping it lets the mark sit on the card itself; in dark mode the Ink Blue strokes (lips,
 * eyelashes) would then vanish into the dark card, so they turn white. Fills such as the pupil sit on the white
 * iris and keep their colour.
 */
export function blendWithCard<T extends LottieData>(data: T, mode: 'light' | 'dark'): T {
  const copy: T = structuredClone(data);
  const isBackdrop = (layer: LottieLayer) =>
    layer.ty === 4 && flatten(layer.shapes).some((s) => s.ty === 'rc' && s.s?.k[0] === copy.w && s.s?.k[1] === copy.h);
  copy.layers = copy.layers.filter((layer) => !isBackdrop(layer));
  if (mode === 'dark') {
    for (const layer of [...copy.layers, ...(copy.assets ?? []).flatMap((a) => a.layers ?? [])]) {
      for (const shape of flatten(layer.shapes)) {
        if (shape.ty === 'st' && shape.c?.a === 0 && isInk(shape.c.k)) shape.c.k = [...WHITE];
      }
    }
  }
  return copy;
}

async function fetchAnimation(src: string): Promise<LottieData> {
  const response = await fetch(src);
  if (!response.ok) throw new Error(`Animation ${src} answered ${response.status}`);
  return response.json();
}

/** The animated DFRNT mark above the login form, with a light and a dark rendition. */
export default function LoginLottie() {
  const { mode } = useColorMode();
  const src = mode === 'dark' ? LOTTIE_DARK : LOTTIE_LIGHT;
  const container = useRef<HTMLDivElement>(null);
  const { data } = useQuery({ queryKey: ['login-lottie', src], queryFn: () => fetchAnimation(src), staleTime: Infinity, retry: false });

  useEffect(() => {
    if (data == null || !container.current) return;
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    let cancelled = false;
    let destroy: (() => void) | undefined;
    // The light player (SVG only, no expression eval) is enough for these files. Only the login
    // page needs it, so it stays out of the app shell chunk.
    void import('lottie-web/build/player/lottie_light').then(({ default: lottie }) => {
      if (cancelled || !container.current) return;
      const animation = lottie.loadAnimation({
        container: container.current,
        renderer: 'svg',
        loop: !reduceMotion,
        autoplay: !reduceMotion,
        animationData: blendWithCard(data, mode),
      });
      destroy = () => animation.destroy();
    });
    return () => {
      cancelled = true;
      destroy?.();
    };
  }, [data, mode]);

  // The box is sized up front so the form doesn't jump when the animation arrives (or never does).
  return <div ref={container} data-testid="login-lottie" data-src={src} aria-hidden="true" style={{ width: SIZE, height: SIZE, margin: '0 auto' }} />;
}
