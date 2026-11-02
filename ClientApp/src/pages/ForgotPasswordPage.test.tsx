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

async function submitEmailOnceCaptchaLoads() {
  const user = userEvent.setup();
  renderWithProviders(<ForgotPasswordPage />);
  await waitFor(() => expect(loadRecaptcha).toHaveBeenCalled());
  await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
  await user.click(screen.getByRole('button', { name: 'Send reset link' }));
}

beforeEach(() => {
  vi.mocked(loadRecaptcha).mockResolvedValue({} as never);
  vi.mocked(getRecaptchaToken).mockResolvedValue('captcha-token');
});

describe('ForgotPasswordPage', () => {
  it('renders the existing copy and a back link', () => {
    server.use(configHandler());
    renderWithProviders(<ForgotPasswordPage />);
    expect(screen.getByRole('heading', { name: 'Forgot password' })).toBeInTheDocument();
    expect(screen.getByText("Enter your email and we'll send you a link to reset your password.")).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to log in' })).toHaveAttribute('href', '/Account/Login');
    const button = screen.getByRole('button', { name: 'Send reset link' });
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
    await submitEmailOnceCaptchaLoads();

    expect(await screen.findByRole('alert')).toHaveTextContent("If an account uses that email, we've sent it a reset link.");
    expect(getRecaptchaToken).toHaveBeenCalledWith('site-key', 'forgot_password');
    expect(bodies).toEqual([{ email: 'jane@example.com', recaptchaToken: 'captcha-token' }]);
    expect(screen.getByLabelText(/^Email/)).toHaveValue('');
  });

  it('shows Sending link… while the request is in flight', async () => {
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
    await submitEmailOnceCaptchaLoads();

    const busy = await screen.findByRole('button', { name: 'Sending link…' });
    expect(busy).toBeDisabled();
    expect(busy.querySelector('svg')).not.toBeNull();
    release();
    await screen.findByRole('alert');
  });

  it('does not post when the captcha cannot be obtained', async () => {
    server.use(configHandler());
    vi.mocked(getRecaptchaToken).mockRejectedValue(new Error('blocked'));
    const bodies = captureForgot();
    await submitEmailOnceCaptchaLoads();

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't send the reset link. Try again in a few minutes.");
    expect(bodies).toHaveLength(0);
  });

  it('does not post when no reCAPTCHA site key is configured', async () => {
    server.use(configHandler(null));
    const bodies = captureForgot();
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await user.type(screen.getByLabelText(/^Email/), 'jane@example.com');
    await user.click(screen.getByRole('button', { name: 'Send reset link' }));

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't send the reset link. Try again in a few minutes.");
    expect(loadRecaptcha).not.toHaveBeenCalled();
    expect(bodies).toHaveLength(0);
  });

  it('shows the server error for a rejected captcha', async () => {
    server.use(configHandler());
    captureForgot(() => HttpResponse.json({ error: "Couldn't confirm you're not a robot. Try again." }, { status: 400 }));
    await submitEmailOnceCaptchaLoads();

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't confirm you're not a robot. Try again.");
    expect(screen.getByRole('button', { name: 'Send reset link' })).toBeEnabled();
  });

  it('validates the email format and clears the error once valid', async () => {
    server.use(configHandler());
    const bodies = captureForgot();
    const user = userEvent.setup();
    renderWithProviders(<ForgotPasswordPage />);
    await user.type(screen.getByLabelText(/^Email/), 'nope');
    expect(screen.getByText('Enter a valid email address, like name@company.com.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Send reset link' }));
    expect(bodies).toHaveLength(0);

    await user.type(screen.getByLabelText(/^Email/), '@example.com');
    expect(screen.queryByText('Enter a valid email address, like name@company.com.')).not.toBeInTheDocument();
  });
});
