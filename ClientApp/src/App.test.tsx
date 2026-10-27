import { http, HttpResponse } from 'msw';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useLocation } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AppRoutes } from './App';
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

describe('routing', () => {
  it('renders the shell and the home tiles for an authenticated user', async () => {
    serveAuthed();
    renderApp('/');
    expect(await screen.findByRole('link', { name: 'Tile 1' })).toBeInTheDocument();
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('contentinfo')).toHaveTextContent(`© ${new Date().getFullYear()} Deliver DFRNT`);
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
    expect(await screen.findByText('Recent records')).toBeInTheDocument();
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
