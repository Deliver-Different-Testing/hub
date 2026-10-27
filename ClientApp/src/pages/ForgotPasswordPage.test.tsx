import { http, HttpResponse } from 'msw';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getRecaptchaToken, loadRecaptcha } from '../lib/recaptcha';
import { antiforgeryHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import ForgotPasswordPage from './ForgotPasswordPage';

vi.mock('../lib/recaptcha', () => ({ loadRecaptcha: vi.fn(), getRecaptchaToken: vi.fn() }));

const configHandler = (key: string | null = 'site-key') =>
  http.get('/api/config', () => HttpResponse.json({ recaptchaSiteKey: key }));

function captureForgot(response: () => Response = () => HttpResponse.json({})) {
  const bodies: unknown[] = [];
  server.use(
    antiforgeryHandler(),
    http.post('/api/auth/forgot-password', async ({ request }) => {
      bodies.push(await request.json());
      return response();
    }),
  );
  return bodies;
}

beforeEach(() => {
  vi.mocked(loadRecaptcha).mockResolvedValue({} as never);
  vi.mocked(getRecaptchaToken).mockResolvedValue('captcha-token');
});

describe('ForgotPasswordPage', () => {
  it('renders the existing copy and a back link', () => {
    server.use(configHandler());
    renderWithProviders(<ForgotPasswordPage />);
    expect(screen.getByRole('heading', { name: 'Forgot Password' })).toBeInTheDocument();
    expect(screen.getByText('Enter your email to reset your password.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to Login' })).toHaveAttribute('href', '/Account/Login');
    const button = screen.getByRole('button', { name: 'Reset Password' });
    expect(button.querySelector('svg')).not.toBeNull();
    expect(button.querySelector('span.button-label')).toBeInTheDocument();
  });

  it('loads reCAPTCHA at runtime with the site key from api/config', async () => {
    server.use(configHandler('abc123'));
    renderWithProviders(<ForgotPasswordPage />);
    await waitFor(() => expect(loadRecaptcha).toHaveBeenCalledWith('abc123'));
  });

  it('sends the email with the captcha token, then shows the success message and clears the field', async () => {
    server.use(configHandler());
    const bodies = captureForgot();
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await waitFor(() => expect(loadRecaptcha).toHaveBeenCalled());
    await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Password reset instructions have been sent to your email.');
    expect(getRecaptchaToken).toHaveBeenCalledWith('site-key', 'forgot_password');
    expect(bodies).toEqual([{ email: 'jane@example.com', recaptchaToken: 'captcha-token' }]);
    expect(screen.getByLabelText(/^Email/)).toHaveValue('');
  });

  it('shows Processing... while the request is in flight', async () => {
    server.use(configHandler());
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    server.use(
      antiforgeryHandler(),
      http.post('/api/auth/forgot-password', async () => {
        await gate;
        return HttpResponse.json({});
      }),
    );
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await waitFor(() => expect(loadRecaptcha).toHaveBeenCalled());
    await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    const busy = await screen.findByRole('button', { name: 'Processing...' });
    expect(busy).toBeDisabled();
    expect(busy.querySelector('svg')).not.toBeNull();
    release();
    await screen.findByRole('alert');
  });

  it('does not post when the captcha cannot be obtained', async () => {
    server.use(configHandler());
    vi.mocked(getRecaptchaToken).mockRejectedValue(new Error('blocked'));
    const bodies = captureForgot();
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await waitFor(() => expect(loadRecaptcha).toHaveBeenCalled());
    await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred. Please try again later.');
    expect(bodies).toHaveLength(0);
  });

  it('does not post when no reCAPTCHA site key is configured', async () => {
    server.use(configHandler(null));
    const bodies = captureForgot();
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred. Please try again later.');
    expect(loadRecaptcha).not.toHaveBeenCalled();
    expect(bodies).toHaveLength(0);
  });

  it('shows the server error for a rejected captcha', async () => {
    server.use(configHandler());
    captureForgot(() => HttpResponse.json({ error: 'reCAPTCHA validation failed. Please try again.' }, { status: 400 }));
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await waitFor(() => expect(loadRecaptcha).toHaveBeenCalled());
    await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('reCAPTCHA validation failed. Please try again.');
    expect(screen.getByRole('button', { name: 'Reset Password' })).toBeEnabled();
  });

  it('validates the email format and clears the error once valid', async () => {
    server.use(configHandler());
    const bodies = captureForgot();
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await user.type(screen.getByLabelText(/^Email/), 'nope');
    expect(screen.getByText('Please enter a valid email address.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));
    expect(bodies).toHaveLength(0);

    await user.type(screen.getByLabelText(/^Email/), '@example.com');
    expect(screen.queryByText('Please enter a valid email address.')).not.toBeInTheDocument();
  });
});
