import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { http, HttpResponse } from 'msw';
import { screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ME_US, homeData, logoHandler, meHandler, tile } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import HomePage, { TILE_STAGGER_MAX_INDEX, TILE_STAGGER_MS } from './HomePage';

const serve = (data = homeData(), me = meHandler()) =>
  server.use(me, logoHandler(), http.get('/api/home', () => HttpResponse.json(data)));

const css = readFileSync(join(__dirname, 'home.module.css'), 'utf8');

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

  // ---- the old App Hub design (Views/Home/Index.cshtml + site.less) ----

  it('frames the header and the grid in one surface, with the logo, the App Hub label and the date under a keyline', async () => {
    serve();
    renderWithProviders(<HomePage />);
    const surface = await screen.findByTestId('hub-surface');
    const header = within(surface).getByTestId('hub-header');
    expect(await within(header).findByRole('img', { name: 'Deliver DFRNT' })).toBeInTheDocument();
    expect(within(header).getByText('App Hub')).toBeInTheDocument();
    expect(within(header).getByText('Tuesday, 27 October')).toBeInTheDocument();
    expect(within(surface).getByRole('link', { name: 'Tile 1' })).toBeInTheDocument();
  });

  it('sits each icon in a tinted well above the title', async () => {
    serve(homeData([tile(1, { title: 'Dispatch' })]));
    renderWithProviders(<HomePage />);
    const link = await screen.findByRole('link', { name: 'Dispatch' });
    const well = link.querySelector('[data-part="icon-well"]');
    expect(well).not.toBeNull();
    expect(well!.querySelector('img')).not.toBeNull();
    expect(link.lastElementChild).toHaveTextContent('Dispatch');
  });

  it('shows the Coming soon chip as its own element over the card', async () => {
    serve(homeData([tile(1, { title: 'Reports', isDisabled: true })]));
    renderWithProviders(<HomePage />);
    const chip = await screen.findByText('Coming soon');
    expect(chip).toHaveAttribute('data-part', 'chip');
  });

  it('accents the cards in Reflex Blue for DFRNT tenants and amber for Urgent, like the old page', async () => {
    // NZ first: the brand only flips once api/me has answered, so this can't pass before it loads.
    serve(homeData(), meHandler());
    const { unmount } = renderWithProviders(<HomePage />);
    await waitFor(() => expect(screen.getByTestId('hub-surface')).toHaveAttribute('data-brand', 'urgent'));
    unmount();

    serve(homeData(), meHandler(ME_US));
    renderWithProviders(<HomePage />);
    await screen.findByRole('link', { name: 'Tile 1' });
    await waitFor(() => expect(screen.getByTestId('hub-surface')).toHaveAttribute('data-brand', 'dfrnt'));
  });

  it('keeps the old card styling: keyline, icon well, hover lift and focus ring', () => {
    expect(css).toMatch(/max-width:\s*960px/);
    expect(css).toMatch(/linear-gradient\(90deg,\s*transparent 0%,\s*var\(--hub-accent\) 30%,\s*var\(--hub-primary\) 70%,\s*transparent 100%\)/);
    expect(css).toMatch(/--hub-primary:\s*var\(--mantine-color-reflex-5\)/);
    expect(css).toMatch(/\[data-brand='urgent'\][\s\S]*--hub-primary:\s*var\(--mantine-color-brand-5\)/);
    expect(css).toMatch(/\.iconWell\s*\{[\s\S]*width:\s*64px/);
    expect(css).toMatch(/translateY\(-2px\)/);
    expect(css).toMatch(/:focus-visible[\s\S]*outline:\s*2px solid var\(--hub-primary\)/);
  });

  it('lays tiles out three across, two on tablets and phones, and one per row on the narrowest screens', () => {
    expect(css).toMatch(/grid-template-columns:\s*repeat\(3, 1fr\)/);
    expect(css).toMatch(/@media \(max-width: 768px\)[\s\S]*grid-template-columns:\s*repeat\(2, 1fr\)/);
    expect(css).toMatch(/@media \(max-width: 380px\)[\s\S]*grid-template-columns:\s*1fr;[\s\S]*grid-template-columns:\s*48px 1fr;/);
  });

  it('puts the Coming soon chip under the title on the narrowest screens, so it neither covers nor crowds it', () => {
    const narrow = css.slice(css.indexOf('@media (max-width: 380px)'));
    expect(narrow).toMatch(/\.card\s*\{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*48px 1fr;/);
    expect(narrow).toMatch(/\.iconWell\s*\{[^}]*grid-row:\s*1 \/ span 2;/);
    expect(narrow).toMatch(/\.chip\s*\{[^}]*position:\s*static;[^}]*grid-column:\s*2;/);
  });
});
