import { http, HttpResponse } from 'msw';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { navigateTo } from '../lib/navigate';
import { antiforgeryHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import LoginPage from './LoginPage';

vi.mock('../lib/navigate', () => ({ navigateTo: vi.fn(), reloadPage: vi.fn() }));

async function fill(user: ReturnType<typeof userEvent.setup>, email = 'jane@example.com', password = 'secret') {
  if (email) await user.type(screen.getByLabelText(/^Email/), email);
  if (password) await user.type(screen.getByLabelText(/^Password/), password);
}

function captureLogin(response: () => Response = () => HttpResponse.json({ redirectUrl: '/' })) {
  const bodies: unknown[] = [];
  server.use(
    antiforgeryHandler(),
    http.post('/api/auth/login', async ({ request }) => {
      bodies.push(await request.json());
      return response();
    }),
  );
  return bodies;
}

describe('LoginPage', () => {
  it('renders the welcome copy, fields and links', () => {
    renderWithProviders(<LoginPage />);
    expect(screen.getByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    expect(screen.getByText('Sign in to your account')).toBeInTheDocument();
    expect(screen.getByLabelText('Login Type')).toHaveValue('false');
    expect(screen.getByRole('option', { name: 'Customer Login' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Courier Login' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Remember me' })).not.toBeChecked();
    expect(screen.getByRole('link', { name: 'Forgot your password?' })).toHaveAttribute('href', '/Account/ForgotPassword');
    expect(document.title).toBe('Log in - Deliver DFRNT');
  });

  // AuthButtonProgressTests: indeterminate progress bar + leading icon with a separate label span.
  it('has an indeterminate progress bar that is hidden until submitting', () => {
    const { container } = renderWithProviders(<LoginPage />);
    const progress = container.querySelector('#loginProgress');
    expect(progress).toHaveAttribute('role', 'progressbar');
    expect(progress).toHaveAttribute('aria-label', 'Signing in');
    expect(progress).toHaveAttribute('aria-hidden', 'true');
    expect(progress?.querySelector('div')).not.toBeNull();
  });

  it('puts a leading icon and a .button-label span in the login button', () => {
    renderWithProviders(<LoginPage />);
    const button = screen.getByRole('button', { name: 'Log in' });
    expect(button.querySelector('svg')).not.toBeNull();
    expect(button.querySelector('span.button-label')).toHaveTextContent('Log in');
  });

  it('posts credentials and follows the returned redirectUrl with a full-page navigation', async () => {
    const bodies = captureLogin(() => HttpResponse.json({ redirectUrl: '/' }));
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await fill(user);
    await user.click(screen.getByRole('checkbox', { name: 'Remember me' }));
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('/'));
    expect(bodies).toEqual([
      { email: 'jane@example.com', password: 'secret', rememberMe: true, isCourierLogin: false },
    ]);
  });

  it('sends the courier login type', async () => {
    const bodies = captureLogin();
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await fill(user);
    await user.selectOptions(screen.getByLabelText('Login Type'), 'true');
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({ isCourierLogin: true, rememberMe: false });
  });

  it('forwards ?returnUrl= and follows a local returnUrl redirect', async () => {
    const bodies = captureLogin(() => HttpResponse.json({ redirectUrl: '/connect/authorize?client_id=x' }));
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />, { route: '/Account/Login?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dx' });
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('/connect/authorize?client_id=x'));
    expect(bodies[0]).toMatchObject({ returnUrl: '/connect/authorize?client_id=x' });
  });

  it('follows an external booking-app redirect', async () => {
    captureLogin(() => HttpResponse.json({ redirectUrl: 'https://booking.example.com/home' }));
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('https://booking.example.com/home'));
  });

  it('keeps the button busy with the progress bar active while submitting', async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    server.use(
      antiforgeryHandler(),
      http.post('/api/auth/login', async () => {
        await gate;
        return HttpResponse.json({ redirectUrl: '/' });
      }),
    );
    const user = userEvent.setup();
    const { container } = renderWithProviders(<LoginPage />);
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    const button = await screen.findByRole('button', { name: 'Logging in...' });
    expect(button).toBeDisabled();
    expect(button.querySelector('svg')).not.toBeNull();
    expect(container.querySelector('#loginProgress')).toHaveAttribute('aria-hidden', 'false');
    release();
    await waitFor(() => expect(navigateTo).toHaveBeenCalled());
  });

  it('shows the server error and re-enables the form on a failed login', async () => {
    captureLogin(() => HttpResponse.json({ error: 'Invalid login attempt.' }, { status: 400 }));
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid login attempt.');
    expect(screen.getByRole('button', { name: 'Log in' })).toBeEnabled();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('falls back to a generic message when the failure has no body', async () => {
    server.use(antiforgeryHandler(), http.post('/api/auth/login', () => new HttpResponse(null, { status: 500 })));
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await fill(user);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid login attempt.');
  });

  it('shows the ?error= banner', () => {
    renderWithProviders(<LoginPage />, { route: '/Account/Login?error=Your%20session%20expired' });
    expect(screen.getByRole('alert')).toHaveTextContent('Your session expired');
  });

  it('validates the email after typing and does not post an invalid one', async () => {
    const bodies = captureLogin();
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await fill(user, 'not-an-email', 'secret');
    expect(screen.getByText('Please enter a valid email address.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(bodies).toHaveLength(0);
    await user.clear(screen.getByLabelText(/^Email/));
    expect(screen.queryByText('Please enter a valid email address.')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/^Email/), 'ok@example.com');
    expect(screen.queryByText('Please enter a valid email address.')).not.toBeInTheDocument();
  });

  it('requires an email and a password', async () => {
    const bodies = captureLogin();
    const user = userEvent.setup();
    renderWithProviders(<LoginPage />);
    await user.click(screen.getByRole('button', { name: 'Log in' }));

    expect(screen.getByText('Please enter a valid email address.')).toBeInTheDocument();
    expect(screen.getByText('Please enter your password.')).toBeInTheDocument();
    expect(bodies).toHaveLength(0);
  });
});
