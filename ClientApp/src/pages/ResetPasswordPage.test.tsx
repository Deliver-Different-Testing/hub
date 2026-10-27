import { http, HttpResponse } from 'msw';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { navigateTo } from '../lib/navigate';
import { PASSWORD_INVALID_MESSAGE, WEAK_PASSWORD_MESSAGE } from '../lib/passwordStrength';
import { antiforgeryHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import ResetPasswordPage from './ResetPasswordPage';

vi.mock('../lib/navigate', () => ({ navigateTo: vi.fn(), reloadPage: vi.fn() }));

const STRONG = 't7#Qm!vZp9$Lw2xR';
const WEAK_BUT_COMPLIANT = 'Password1!';

const validCode = (email = 'jane@example.com') =>
  http.get('/api/auth/reset-password/CODE123', () => HttpResponse.json({ email }));

const renderValid = async () => {
  server.use(validCode());
  const view = renderWithProviders(<ResetPasswordPage />, { route: '/Account/ResetPassword?code=CODE123' });
  await screen.findByRole('heading', { name: 'Reset Password' });
  await screen.findByLabelText(/^New Password/);
  return view;
};

describe('ResetPasswordPage', () => {
  it('validates the code and greets the account by email', async () => {
    await renderValid();
    expect(screen.getByText('jane@example.com')).toBeInTheDocument();
    expect(screen.getByText(/Reset your password for/)).toBeInTheDocument();
    expect(document.title).toBe('Reset Password - Deliver DFRNT');
  });

  it('shows an invalid-link message with a way back when the code is rejected', async () => {
    server.use(http.get('/api/auth/reset-password/BAD', () => HttpResponse.json({ error: 'nope' }, { status: 404 })));
    renderWithProviders(<ResetPasswordPage />, { route: '/Account/ResetPassword?code=BAD' });
    expect(await screen.findByRole('alert')).toHaveTextContent('This password reset link is invalid or has expired.');
    expect(screen.getByRole('link', { name: 'Request a new link' })).toHaveAttribute('href', '/Account/ForgotPassword');
  });

  it('treats a missing code as invalid without calling the API', () => {
    renderWithProviders(<ResetPasswordPage />, { route: '/Account/ResetPassword' });
    expect(screen.getByRole('alert')).toHaveTextContent('invalid or has expired');
  });

  // ResetPasswordStrengthTests.ResetView_HasStrengthMeterMarkup
  it('hides the strength meter until a password is typed, then shows bar, label and feedback', async () => {
    const user = userEvent.setup();
    const { container } = await renderValid();
    expect(container.querySelector('#passwordStrength')).toBeNull();

    await user.type(screen.getByLabelText(/^New Password/), STRONG);
    await waitFor(() => expect(container.querySelector('#passwordStrength')).not.toBeNull());
    expect(screen.getByRole('progressbar', { name: 'Password strength' })).toBeInTheDocument();
    expect(container.querySelector('#strengthLabel')).toHaveTextContent(/Good|Strong/);
    expect(container.querySelector('#strengthFeedback')).toHaveAttribute('aria-live', 'polite');

    await user.clear(screen.getByLabelText(/^New Password/));
    expect(container.querySelector('#passwordStrength')).toBeNull();
  });

  it('enforces the composition rule that mirrors the backend', async () => {
    const user = userEvent.setup();
    await renderValid();
    await user.type(screen.getByLabelText(/^New Password/), 'abc');
    expect(screen.getByText(PASSWORD_INVALID_MESSAGE)).toBeInTheDocument();
  });

  it('adds the zxcvbn strength gate on top of the composition rule', async () => {
    const user = userEvent.setup();
    const { container } = await renderValid();
    await user.type(screen.getByLabelText(/^New Password/), WEAK_BUT_COMPLIANT);
    expect(await screen.findByText(WEAK_PASSWORD_MESSAGE)).toBeInTheDocument();
    expect(container.querySelector('#strengthLabel')).toHaveTextContent(/Very weak|Weak|Fair/);
  });

  it('flags a confirm password that does not match', async () => {
    const user = userEvent.setup();
    await renderValid();
    await user.type(screen.getByLabelText(/^New Password/), STRONG);
    await user.type(screen.getByLabelText(/^Confirm Password/), 'different');
    expect(screen.getByText("Passwords don't match")).toBeInTheDocument();
  });

  it('does not submit a weak or empty password', async () => {
    const posts: unknown[] = [];
    server.use(antiforgeryHandler(), http.post('/api/auth/reset-password', async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json({ redirectUrl: '/' });
    }));
    const user = userEvent.setup();
    await renderValid();
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));
    expect(screen.getByText('Please enter a new password.')).toBeInTheDocument();
    expect(screen.getByText('Please confirm your password.')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/^New Password/), WEAK_BUT_COMPLIANT);
    await user.type(screen.getByLabelText(/^Confirm Password/), WEAK_BUT_COMPLIANT);
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));
    expect(await screen.findByText(WEAK_PASSWORD_MESSAGE)).toBeInTheDocument();
    expect(posts).toHaveLength(0);
  });

  it('does not submit a password that fails the composition rule or mismatches', async () => {
    const posts: unknown[] = [];
    server.use(antiforgeryHandler(), http.post('/api/auth/reset-password', async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json({ redirectUrl: '/' });
    }));
    const user = userEvent.setup();
    await renderValid();
    await user.type(screen.getByLabelText(/^New Password/), 'abc');
    await user.type(screen.getByLabelText(/^Confirm Password/), 'abc');
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));
    expect(posts).toHaveLength(0);
  });

  it('posts a strong matching password and signs in via the returned redirectUrl', async () => {
    const posts: unknown[] = [];
    server.use(antiforgeryHandler(), http.post('/api/auth/reset-password', async ({ request }) => {
      posts.push(await request.json());
      return HttpResponse.json({ redirectUrl: '/' });
    }));
    const user = userEvent.setup();
    await renderValid();
    await user.type(screen.getByLabelText(/^New Password/), STRONG);
    await user.type(screen.getByLabelText(/^Confirm Password/), STRONG);
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    await waitFor(() => expect(navigateTo).toHaveBeenCalledWith('/'));
    expect(posts).toEqual([{ code: 'CODE123', password: STRONG, confirmPassword: STRONG }]);
    const button = screen.getByRole('button', { name: 'Processing...' });
    expect(button.querySelector('svg')).not.toBeNull();
  });

  it('shows the server error and re-enables the button when the reset fails', async () => {
    server.use(antiforgeryHandler(), http.post('/api/auth/reset-password', () =>
      HttpResponse.json({ error: 'Reset failed' }, { status: 400 })));
    const user = userEvent.setup();
    await renderValid();
    await user.type(screen.getByLabelText(/^New Password/), STRONG);
    await user.type(screen.getByLabelText(/^Confirm Password/), STRONG);
    await user.click(screen.getByRole('button', { name: 'Reset Password' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Reset failed');
    expect(screen.getByRole('button', { name: 'Reset Password' })).toBeEnabled();
  });

  it('puts a leading icon and a .button-label span in the reset button, centred', async () => {
    await renderValid();
    const button = screen.getByRole('button', { name: 'Reset Password' });
    expect(button.querySelector('svg')).not.toBeNull();
    expect(button.querySelector('span.button-label')).toBeInTheDocument();
    expect(button.closest('[class*="Center"]')).not.toBeNull();
  });
});
