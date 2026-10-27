import { http, HttpResponse } from 'msw';
import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import LoginLottie, { blendWithCard, LOTTIE_DARK, LOTTIE_LIGHT } from './LoginLottie';

// lottie-web needs a real SVG layout engine, which jsdom doesn't have.
const { loadAnimation, destroy } = vi.hoisted(() => ({ loadAnimation: vi.fn(), destroy: vi.fn() }));
vi.mock('lottie-web/build/player/lottie_light', () => ({ default: { loadAnimation } }));

const animation = { v: '5.7.4', layers: [] };

// The shape of the real files: an Ink Blue stroke, a precomp and a full-canvas backdrop rectangle at the bottom.
const INK = [0.0706, 0.0667, 0.1686];
const group = (item: { ty: string; c: { a: number; k: number[] } }) => ({ ty: 'gr', it: [item] });
const colour = (layer?: { shapes?: { it: { c?: { k: number[] } }[] }[] }) => layer?.shapes?.[0]?.it[0]?.c?.k;
function rectLayer(nm: string, size: number) {
  return { ty: 4, nm, shapes: [{ ty: 'gr', it: [{ ty: 'rc', s: { a: 0, k: [size, size] } }, { ty: 'fl', c: { a: 0, k: [1, 1, 1] } }] }] };
}
const withBackdrop = {
  v: '5.7.4',
  w: 2400,
  h: 2400,
  layers: [
    { ty: 4, nm: 'Lip L', shapes: [group({ ty: 'st', c: { a: 0, k: INK } })] },
    { ty: 0, nm: 'Eye', refId: '1' },
    rectLayer('Shape Layer 1', 2400),
  ],
  assets: [
    {
      id: '1',
      layers: [
        { ty: 4, nm: 'Eyelash', shapes: [group({ ty: 'st', c: { a: 0, k: INK } })] },
        { ty: 4, nm: 'Pupil', shapes: [group({ ty: 'fl', c: { a: 0, k: INK } })] },
      ],
    },
  ],
};

function stubReducedMotion(reduce: boolean) {
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query: string) =>
      ({
        matches: reduce && query.includes('prefers-reduced-motion'),
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        onchange: null,
        dispatchEvent: vi.fn(),
      }) as MediaQueryList,
  );
}

describe('LoginLottie', () => {
  beforeEach(() => {
    loadAnimation.mockReset().mockReturnValue({ destroy });
    destroy.mockReset();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('loads the light animation in light mode and loops it', async () => {
    const requested: string[] = [];
    server.use(
      http.get('/images/:file', ({ request }) => {
        requested.push(new URL(request.url).pathname);
        return HttpResponse.json(animation);
      }),
    );
    renderWithProviders(<LoginLottie />);

    await waitFor(() => expect(loadAnimation).toHaveBeenCalledTimes(1));
    expect(screen.getByTestId('login-lottie')).toHaveAttribute('data-src', LOTTIE_LIGHT);
    expect(requested).toEqual([LOTTIE_LIGHT]);
    expect(loadAnimation).toHaveBeenCalledWith(
      expect.objectContaining({
        container: screen.getByTestId('login-lottie'),
        renderer: 'svg',
        loop: true,
        autoplay: true,
        animationData: animation,
      }),
    );
  });

  it('loads the dark animation in dark mode', async () => {
    localStorage.setItem('hub-color-mode', 'dark');
    server.use(http.get('/images/:file', () => HttpResponse.json(animation)));
    renderWithProviders(<LoginLottie />);

    await waitFor(() => expect(loadAnimation).toHaveBeenCalledTimes(1));
    expect(screen.getByTestId('login-lottie')).toHaveAttribute('data-src', LOTTIE_DARK);
  });

  it('holds still for users who prefer reduced motion', async () => {
    stubReducedMotion(true);
    server.use(http.get('/images/:file', () => HttpResponse.json(animation)));
    renderWithProviders(<LoginLottie />);

    await waitFor(() => expect(loadAnimation).toHaveBeenCalledTimes(1));
    expect(loadAnimation).toHaveBeenCalledWith(expect.objectContaining({ loop: false, autoplay: false }));
  });

  it('tears the animation down when the page unmounts', async () => {
    server.use(http.get('/images/:file', () => HttpResponse.json(animation)));
    const { unmount } = renderWithProviders(<LoginLottie />);
    await waitFor(() => expect(loadAnimation).toHaveBeenCalledTimes(1));

    unmount();
    expect(destroy).toHaveBeenCalledTimes(1);
  });

  it('drops the solid backdrop so the mark sits straight on the card', async () => {
    server.use(http.get('/images/:file', () => HttpResponse.json(withBackdrop)));
    renderWithProviders(<LoginLottie />);

    await waitFor(() => expect(loadAnimation).toHaveBeenCalledTimes(1));
    const { animationData } = loadAnimation.mock.calls[0][0];
    expect(animationData.layers.map((l: { nm: string }) => l.nm)).toEqual(['Lip L', 'Eye']);
  });

  it('keeps every layer if there is no full-canvas backdrop', () => {
    const data = { w: 100, h: 100, layers: [rectLayer('Small', 50)] };
    expect(blendWithCard(data, 'light').layers).toHaveLength(1);
  });

  it('lightens the Ink Blue strokes in dark mode so they stay visible on the dark card, leaving fills alone', () => {
    const blended = blendWithCard(withBackdrop, 'dark');
    expect(colour(blended.layers[0])).toEqual([1, 1, 1]);
    expect(colour(blended.assets[0]?.layers[0])).toEqual([1, 1, 1]);
    expect(colour(blended.assets[0]?.layers[1])).toEqual(INK);
    // The fetched data is cached by react-query, so it must not be mutated.
    expect(colour(withBackdrop.layers[0])).toEqual(INK);
  });

  it('keeps the Ink Blue strokes in light mode', () => {
    expect(colour(blendWithCard(withBackdrop, 'light').layers[0])).toEqual(INK);
  });

  it('keeps its space but shows nothing if the animation fails to load', async () => {
    server.use(http.get('/images/:file', () => new HttpResponse(null, { status: 404 })));
    renderWithProviders(<LoginLottie />);

    expect(screen.getByTestId('login-lottie')).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(loadAnimation).not.toHaveBeenCalled();
  });
});
