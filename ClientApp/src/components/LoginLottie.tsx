import { useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useColorMode } from '../hooks/useColorMode';

export const LOTTIE_LIGHT = '/images/Auto-mate-Ready%20state%20(2).json';
export const LOTTIE_DARK = '/images/Auto-mate-Ready%20state%20(2)_dark.json';
const SIZE = 200;

async function fetchAnimation(src: string): Promise<unknown> {
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
        animationData: data,
      });
      destroy = () => animation.destroy();
    });
    return () => {
      cancelled = true;
      destroy?.();
    };
  }, [data]);

  // The box is sized up front so the form doesn't jump when the animation arrives (or never does).
  return <div ref={container} data-testid="login-lottie" data-src={src} aria-hidden="true" style={{ width: SIZE, height: SIZE, margin: '0 auto' }} />;
}
