import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { http, HttpResponse } from 'msw';
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { homeData, logoHandler, meHandler, tile } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import HomePage, { TILE_STAGGER_MAX_INDEX, TILE_STAGGER_MS } from './HomePage';

const serve = (data = homeData()) =>
  server.use(meHandler(), logoHandler(), http.get('/api/home', () => HttpResponse.json(data)));

describe('HomePage', () => {
  it('renders the App Hub header with the date from the tenant wall-clock', async () => {
    serve();
    renderWithProviders(<HomePage />);
    expect(await screen.findByText('Tuesday, 27 October')).toBeInTheDocument();
    expect(screen.getByText('App Hub')).toBeInTheDocument();
    expect(document.title).toBe('App Hub - Deliver DFRNT');
  });

  it('renders enabled tiles as new-tab links with an <img> icon from the API path', async () => {
    serve(homeData([tile(1, { title: 'Dispatch', href: 'https://dispatch.example.com', icon: '/images/icons/dispatch.svg' })]));
    renderWithProviders(<HomePage />);
    const link = await screen.findByRole('link', { name: 'Dispatch' });
    expect(link).toHaveAttribute('href', 'https://dispatch.example.com');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
    const img = link.querySelector('img');
    expect(img).toHaveAttribute('src', '/images/icons/dispatch.svg');
    expect(img).toHaveAttribute('alt', '');
  });

  it('renders disabled tiles as non-links marked "Coming soon"', async () => {
    serve(homeData([tile(1, { title: 'Reports', isDisabled: true }), tile(2, { title: 'Dispatch' })]));
    renderWithProviders(<HomePage />);
    await screen.findByRole('link', { name: 'Dispatch' });

    expect(screen.queryByRole('link', { name: /Reports/ })).not.toBeInTheDocument();
    const soon = screen.getByTitle('Coming soon');
    expect(soon).toHaveAttribute('aria-disabled', 'true');
    expect(soon).toHaveTextContent('Reports');
    expect(soon).toHaveTextContent('Coming soon');
  });

  // HomeAppGridAnimationTests: every tile the catalogue can render (currently 18) gets its own stagger delay.
  it('staggers the entrance animation so every tile trails the previous one', async () => {
    const tiles = Array.from({ length: 18 }, (_, i) => tile(i + 1));
    serve(homeData(tiles));
    renderWithProviders(<HomePage />);
    await screen.findByRole('link', { name: 'Tile 1' });

    const delays = tiles.map((t) => Number.parseInt(document.querySelector<HTMLElement>(`[data-tile="${t.key}"]`)!.style.animationDelay, 10));
    delays.forEach((d, i) => expect(d).toBe(i * TILE_STAGGER_MS));
    expect(new Set(delays).size).toBe(tiles.length);
    expect(TILE_STAGGER_MAX_INDEX).toBeGreaterThanOrEqual(tiles.length - 1);
  });

  it('caps the stagger so a very long list does not wait for seconds', async () => {
    const tiles = Array.from({ length: 40 }, (_, i) => tile(i + 1));
    serve(homeData(tiles));
    renderWithProviders(<HomePage />);
    await screen.findByRole('link', { name: 'Tile 1' });
    const last = document.querySelector<HTMLElement>('[data-tile="hub-tile-40"]')!;
    expect(last.style.animationDelay).toBe(`${TILE_STAGGER_MAX_INDEX * TILE_STAGGER_MS}ms`);
  });

  it('animates tiles in and respects prefers-reduced-motion', () => {
    const css = readFileSync(join(__dirname, 'home.module.css'), 'utf8');
    expect(css).toMatch(/animation:\s*tileIn/);
    expect(css).toMatch(/@keyframes tileIn/);
    expect(css).toMatch(/@media \(prefers-reduced-motion: reduce\)[\s\S]*animation: none/);
  });

  it('shows an error when the tiles cannot be loaded', async () => {
    server.use(meHandler(), logoHandler(), http.get('/api/home', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<HomePage />);
    expect(await screen.findByText("Couldn't load your apps. Try again.")).toBeInTheDocument();
  });
});
