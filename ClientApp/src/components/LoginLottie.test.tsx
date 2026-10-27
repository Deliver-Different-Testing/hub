import { http, HttpResponse } from 'msw';
import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import LoginLottie, { LOTTIE_DARK, LOTTIE_LIGHT } from './LoginLottie';

// lottie-web needs a real SVG layout engine, which jsdom doesn't have.
const { loadAnimation, destroy } = vi.hoisted(() => ({ loadAnimation: vi.fn(), destroy: vi.fn() }));
vi.mock('lottie-web/build/player/lottie_light', () => ({ default: { loadAnimation } }));

const animation = { v: '5.7.4', layers: [] };

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

  it('keeps its space but shows nothing if the animation fails to load', async () => {
    server.use(http.get('/images/:file', () => new HttpResponse(null, { status: 404 })));
    renderWithProviders(<LoginLottie />);

    expect(screen.getByTestId('login-lottie')).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(loadAnimation).not.toHaveBeenCalled();
  });
});
