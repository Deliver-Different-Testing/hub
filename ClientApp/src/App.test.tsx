import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { http, HttpResponse } from 'msw';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AppRoutes } from './App';
import { APP_BAR_HEIGHT_PX } from './components/appBarChrome';
import { navigateTo, reloadPage } from './lib/navigate';
import {
  ME_NZ,
  ME_US,
  antiforgeryHandler,
  fuelData,
  homeData,
  logoHandler,
  meHandler,
  unauthorizedMeHandler,
} from './test/handlers';
import { server } from './test/server';
import { renderWithProviders } from './test/utils';

vi.mock('./lib/navigate', () => ({ navigateTo: vi.fn(), reloadPage: vi.fn() }));

function Probe() {
  const { pathname, search } = useLocation();
  return <div data-testid="location">{pathname + search}</div>;
}

const renderApp = (route: string) =>
  renderWithProviders(
    <>
      <AppRoutes />
      <Probe />
    </>,
    { route },
  );

const serveAuthed = (me = ME_NZ) =>
  server.use(
    meHandler(me),
    logoHandler(),
    http.get('/api/home', () => HttpResponse.json(homeData())),
    http.get('/api/fuel-surcharge', () => HttpResponse.json(fuelData())),
  );

describe('home page backdrop', () => {
  // The old App Hub page painted a tiled pattern with soft glows behind everything (site.less body#home).
  it('puts the App Hub backdrop behind the shell on the home page only', async () => {
    serveAuthed();
    renderApp('/');
    await screen.findByRole('link', { name: 'Tile 1' });
    const shell = screen.getByTestId('app-shell');
    expect(shell.className).toMatch(/homeField/);
    // An inline background shorthand (Mantine's bg prop) would wipe the class's background-image.
    expect(shell.style.background).toBe('');
    await waitFor(() => expect(shell).toHaveAttribute('data-brand', 'urgent'));
  });

  it('leaves other pages on the plain page surface', async () => {
    serveAuthed();
    renderApp('/FuelSurcharge');
    await screen.findByRole('banner');
    expect(screen.getByTestId('app-shell').className).not.toMatch(/homeField/);
  });

  it('keeps the old pattern and glows, flipping the strokes to white in dark mode', () => {
    const css = readFileSync(join(__dirname, 'styles/homeBackground.module.css'), 'utf8');
    expect(css).toMatch(/url\("data:image\/svg\+xml[^"]*stroke='%231e293b'/);
    expect(css).toMatch(/\[data-mantine-color-scheme='dark'\][\s\S]*stroke='%23ffffff'/);
    expect(css).toMatch(/background-attachment:\s*fixed, fixed, fixed, fixed/);
  });
});

describe('routing', () => {
  it('renders the shell and the home tiles for an authenticated user', async () => {
    serveAuthed();
    renderApp('/');
    expect(await screen.findByRole('link', { name: 'Tile 1' })).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('contentinfo')).toHaveTextContent(`© ${new Date().getFullYear()} Deliver DFRNT`);
  });

  // site.less .footer / .footer-row on the Razor layout.
  it('gives the footer the old Hub look: a full-width surface bar with centred, small secondary text', async () => {
    serveAuthed();
    renderApp('/');
    const footer = await screen.findByRole('contentinfo');
    expect(footer.className).toMatch(/footer/);
    expect(within(footer).getByText(/Deliver DFRNT/).className).toMatch(/row/);

    const css = readFileSync(join(__dirname, 'components/footer.module.css'), 'utf8');
    expect(css).toMatch(/\.footer\s*\{[^}]*background:\s*var\(--dd-surface-container\)/);
    expect(css).toMatch(/\.footer\s*\{[^}]*box-shadow:\s*0 -1px 3px/);
    expect(css).toMatch(/\.footer\s*\{[^}]*padding:\s*0\.75rem 0/);
    expect(css).toMatch(/\.row\s*\{[^}]*justify-content:\s*center/);
    expect(css).toMatch(/\.row\s*\{[^}]*font-size:\s*0\.8125rem/);
    expect(css).toMatch(/\.row\s*\{[^}]*font-weight:\s*500/);
    expect(css).toMatch(/\.row\s*\{[^}]*color:\s*var\(--dd-on-surface-secondary\)/);
  });

  it.each([
    ['/Account/Login', 'Welcome back'],
    ['/account/forgotpassword', 'Forgot Password'],
  ])('renders the anonymous page %s without the shell or api/me', async (route, heading) => {
    renderApp(route);
    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
  });

  it('redirects an unknown path to home', async () => {
    serveAuthed();
    renderApp('/definitely/not/a/page');
    expect(await screen.findByRole('link', { name: 'Tile 1' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/$/);
  });

  it('redirects to the login page with the return path when api/me is 401', async () => {
    server.use(unauthorizedMeHandler());
    renderApp('/FuelSurcharge?x=1');
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/Account/Login?returnUrl=%2FFuelSurcharge%3Fx%3D1');
  });

  it('redirects to login when another API call returns 401 on an authenticated route', async () => {
    server.use(meHandler(), logoHandler(), http.get('/api/home', () => HttpResponse.json({}, { status: 401 })));
    renderApp('/');
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/Account/Login?returnUrl=%2F');
  });

  it('offers a retry when api/me fails for another reason', async () => {
    let calls = 0;
    server.use(
      logoHandler(),
      http.get('/api/me', () => (++calls === 1 ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(ME_NZ))),
      http.get('/api/home', () => HttpResponse.json(homeData())),
    );
    const user = userEvent.setup();
    renderApp('/');
    expect(await screen.findByText("Couldn't load your account. Try again.")).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByRole('link', { name: 'Tile 1' })).toBeInTheDocument();
  });

  it('routes to the settings and fuel surcharge pages', async () => {
    serveAuthed();
    server.use(http.get('/api/me/settings', () => HttpResponse.json([])));
    renderApp('/FuelSurcharge');
    expect(await screen.findByText('Rate history')).toBeInTheDocument();
  });
});

// LayoutTenantSwitchingTests: org switcher behaviour.
describe('AppShell org switcher', () => {
  const openSwitcher = async (user: ReturnType<typeof userEvent.setup>) => {
    await user.click(await screen.findByRole('button', { name: 'Urgent' }));
    return screen.findByRole('menu');
  };

  it('labels the current organization and lists every tenant with the current one marked', async () => {
    serveAuthed();
    const user = userEvent.setup();
    renderApp('/');
    expect(await screen.findByText('Organizations:')).toBeInTheDocument();
    const menu = await openSwitcher(user);
    const items = within(menu).getAllByRole('menuitem');
    expect(items.map((i) => i.textContent)).toEqual(['Urgent', 'Acme Delivery']);
    expect(items[0]).toHaveAttribute('aria-current', 'true');
    expect(items[1]).toHaveAttribute('data-tenant-id', '2');
    expect(items[1]).toHaveAttribute('data-tenant-code', 'acme');
  });

  it('follows the redirectUrl the backend returns (cross-app switch token)', async () => {
    serveAuthed();
    const bodies: unknown[] = [];
    server.use(
      antiforgeryHandler(),
      http.put('/api/me/current-tenant', async ({ request }) => {
        bodies.push(await request.json());
        return HttpResponse.json({ redirectUrl: 'https://acme.example.com/Account/AcceptTenantSwitchToken?t=abc' });
      }),
    );
    const user = userEvent.setup();
    renderApp('/');
    await user.click(within(await openSwitcher(user)).getByRole('menuitem', { name: 'Acme Delivery' }));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('https://acme.example.com/Account/AcceptTenantSwitchToken?t=abc'));
    expect(bodies).toEqual([{ tenantId: 2 }]);
    expect(reloadPage).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Acme Delivery' })).toBeDisabled();
  });

  it('reloads in place when no redirectUrl is returned', async () => {
    serveAuthed();
    server.use(antiforgeryHandler(), http.put('/api/me/current-tenant', () => HttpResponse.json({ redirectUrl: null })));
    const user = userEvent.setup();
    renderApp('/');
    await user.click(within(await openSwitcher(user)).getByRole('menuitem', { name: 'Acme Delivery' }));
    await waitFor(() => expect(reloadPage).toHaveBeenCalled());
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('shows the actionable server message when the switch fails and re-enables the switcher', async () => {
    serveAuthed();
    server.use(
      antiforgeryHandler(),
      http.put('/api/me/current-tenant', () =>
        HttpResponse.json({ error: "Your account isn't set up in that tenant yet." }, { status: 400 })),
    );
    const user = userEvent.setup();
    renderApp('/');
    await user.click(within(await openSwitcher(user)).getByRole('menuitem', { name: 'Acme Delivery' }));

    expect(await screen.findByText("Your account isn't set up in that tenant yet.")).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: 'Urgent' })).toBeEnabled();
    expect(reloadPage).not.toHaveBeenCalled();
  });

  it('falls back to a default message when the failure carries none', async () => {
    serveAuthed();
    server.use(antiforgeryHandler(), http.put('/api/me/current-tenant', () => new HttpResponse(null, { status: 500 })));
    const user = userEvent.setup();
    renderApp('/');
    await user.click(within(await openSwitcher(user)).getByRole('menuitem', { name: 'Acme Delivery' }));
    expect(await screen.findByText('Could not switch tenant. Please try again.')).toBeInTheDocument();
  });

  it('shows "Select Tenant" when the current tenant is not in the list, and hides the switcher with no tenants', async () => {
    serveAuthed({ ...ME_US, currentTenant: { id: 99, name: 'Gone', code: 'gone', countryCode: 'US' } });
    const first = renderApp('/');
    expect(await screen.findByRole('button', { name: 'Select Tenant' })).toBeInTheDocument();
    first.unmount();

    serveAuthed({ ...ME_US, tenants: [] });
    renderApp('/');
    await screen.findByRole('link', { name: 'Tile 1' });
    expect(screen.queryByText('Organizations:')).not.toBeInTheDocument();
  });
});

// IntegrationManager's bar size, the old Hub navbar's look.
describe('AppShell app bar', () => {
  it('is a full-width 56px bar with the logo linking home and no breadcrumb', async () => {
    serveAuthed();
    renderApp('/FuelSurcharge');
    const bar = await screen.findByRole('banner');
    expect(bar).toHaveStyle({ height: `${APP_BAR_HEIGHT_PX}px` });
    expect(bar.closest('.mantine-Container-root')).toBeNull();
    expect(within(bar).getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/');
    expect(bar.querySelector('.mantine-Breadcrumbs-root')).toBeNull();
  });

  it('keeps the old order: organization switcher, profile, then theme', async () => {
    serveAuthed();
    renderApp('/');
    const bar = await screen.findByRole('banner');
    await within(bar).findByRole('button', { name: 'Urgent' });
    const names = within(bar).getAllByRole('button').map((b) => b.getAttribute('aria-label') ?? b.textContent);
    expect(names).toEqual(['Urgent', 'Account menu', 'Change theme']);
  });

  it('styles the switcher as the old translucent pill and the theme toggle as a bare round button', async () => {
    serveAuthed();
    renderApp('/');
    expect((await screen.findByRole('button', { name: 'Urgent' })).className).toMatch(/tenantChip/);
    expect(screen.getByText('Organizations:').className).toMatch(/tenantLabel/);
    expect(screen.getByRole('button', { name: 'Account menu' }).className).toMatch(/profile/);
    expect(screen.getByRole('button', { name: 'Change theme' }).className).toMatch(/iconButton/);
  });

  it('paints the bar chrome from the brand scrim tokens', async () => {
    serveAuthed();
    renderApp('/');
    const bar = await screen.findByRole('banner');
    for (const v of ['--app-bar-on', '--app-bar-chip', '--app-bar-chip-hover', '--app-bar-wash']) {
      expect(bar.style.getPropertyValue(v)).not.toBe('');
    }
  });

  it('flips the profile caret while the menu is open', () => {
    const css = readFileSync(join(__dirname, 'components/appBar.module.css'), 'utf8');
    expect(css).toMatch(/\.profile\[data-expanded\] \.caret\s*\{\s*transform: rotate\(180deg\)/);
    expect(css).toMatch(/text-transform: uppercase/);
  });
});

describe('AppShell profile menu', () => {
  it('shows the display name, the email, User Settings and Logout', async () => {
    serveAuthed();
    const user = userEvent.setup();
    renderApp('/');
    await user.click(await screen.findByRole('button', { name: 'Account menu' }));

    expect(screen.getByRole('button', { name: 'Account menu' })).toHaveTextContent('Jane Doe');
    expect(await screen.findByText('jane.doe@example.com')).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'User Settings' })).toHaveAttribute('href', '/Account/Settings');
    expect(screen.getByRole('menuitem', { name: 'Logout' })).toBeInTheDocument();
  });

  it('derives a name from the email when the account has no display name', async () => {
    serveAuthed({ ...ME_NZ, displayName: null });
    renderApp('/');
    expect(await screen.findByRole('button', { name: 'Account menu' })).toHaveTextContent('Jane Doe');
  });

  it('logs out through the API and follows the returned redirect', async () => {
    serveAuthed();
    server.use(antiforgeryHandler(), http.post('/api/auth/logout', () => HttpResponse.json({ redirectUrl: '/Account/Login' })));
    const user = userEvent.setup();
    renderApp('/');
    await user.click(await screen.findByRole('button', { name: 'Account menu' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('/Account/Login'));
  });

  it('falls back to the login page when logout returns no redirect and shows an error on failure', async () => {
    serveAuthed();
    server.use(antiforgeryHandler(), http.post('/api/auth/logout', () => HttpResponse.json({ redirectUrl: null })));
    const user = userEvent.setup();
    const first = renderApp('/');
    await user.click(await screen.findByRole('button', { name: 'Account menu' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('/Account/Login'));
    first.unmount();

    server.use(http.post('/api/auth/logout', () => new HttpResponse(null, { status: 500 })));
    renderApp('/');
    await user.click(await screen.findByRole('button', { name: 'Account menu' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    expect(await screen.findByText(/Couldn't reach the server/)).toBeInTheDocument();
  });
});

describe('AppShell colour mode', () => {
  it('has a colour toggle that persists across a remount', async () => {
    serveAuthed();
    const user = userEvent.setup();
    const first = renderApp('/');
    await user.click(await screen.findByRole('button', { name: 'Change theme' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Dark' }));
    expect(window.localStorage.getItem('hub-color-mode')).toBe('dark');
    first.unmount();

    renderApp('/');
    await user.click(await screen.findByRole('button', { name: 'Change theme' }));
    expect(await screen.findByRole('menuitem', { name: 'Dark' })).toHaveAttribute('aria-current', 'true');
  });
});
