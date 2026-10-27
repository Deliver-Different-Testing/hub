import { http, HttpResponse } from 'msw';
import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { antiforgeryHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import SettingsPage, { COPIED_RESET_DELAY_MS } from './SettingsPage';

const settings = (apiKey: string | null = null) => [
  { id: 1, name: 'APIKey', value: apiKey },
  { id: 2, name: 'DefaultPrinter', value: 'Front desk' },
  { id: 3, name: 'Timezone', value: 'Pacific/Auckland' },
];
const serve = (apiKey: string | null = null) =>
  server.use(http.get('/api/me/settings', () => HttpResponse.json(settings(apiKey))));

describe('SettingsPage', () => {
  it('lists the other settings, excluding the API key row', async () => {
    serve();
    renderWithProviders(<SettingsPage />);
    expect(await screen.findByRole('heading', { name: 'User Settings' })).toBeInTheDocument();
    expect(document.title).toBe('Settings - Deliver DFRNT');
    expect(screen.getByRole('heading', { name: 'API Key' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Other Settings' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Setting' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Value' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'DefaultPrinter' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'Front desk' })).toBeInTheDocument();
    expect(screen.queryByRole('cell', { name: 'APIKey' })).not.toBeInTheDocument();
  });

  it('offers Generate Key when there is no key, and Copy when there is one', async () => {
    serve();
    const { unmount } = renderWithProviders(<SettingsPage />);
    expect(await screen.findByRole('button', { name: 'Generate Key' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Copy/ })).not.toBeInTheDocument();
    unmount();

    serve('existing-key');
    renderWithProviders(<SettingsPage />);
    expect(await screen.findByDisplayValue('existing-key')).toHaveAttribute('readonly');
    expect(screen.getByRole('button', { name: /Copy/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Generate Key' })).not.toBeInTheDocument();
  });

  it('asks for confirmation in a dialog and does nothing when cancelled', async () => {
    serve();
    const posts = vi.fn();
    server.use(antiforgeryHandler(), http.post('/api/me/api-key', () => {
      posts();
      return HttpResponse.json({ apiKey: 'k' });
    }));
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />);
    await user.click(await screen.findByRole('button', { name: 'Generate Key' }));

    const dialog = await screen.findByRole('dialog', { name: 'Generate API key' });
    expect(dialog).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(posts).not.toHaveBeenCalled();
  });

  it('generates a key after confirming, shows it, and swaps to the Copy button', async () => {
    serve();
    server.use(antiforgeryHandler(), http.post('/api/me/api-key', () => HttpResponse.json({ apiKey: 'new-key-123' })));
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />);
    await user.click(await screen.findByRole('button', { name: 'Generate Key' }));
    const dialog = await screen.findByRole('dialog');
    await user.click(within(dialog).getByRole('button', { name: 'Generate Key' }));

    expect(await screen.findByDisplayValue('new-key-123')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Copy/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Generate Key' })).not.toBeInTheDocument();
  });

  it('shows an inline error when generation fails', async () => {
    serve();
    server.use(antiforgeryHandler(), http.post('/api/me/api-key', () => HttpResponse.json({ error: 'Failed to generate API key' }, { status: 400 })));
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />);
    await user.click(await screen.findByRole('button', { name: 'Generate Key' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Generate Key' }));

    expect(await screen.findByText('Failed to generate API key')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Generate Key' })).toBeInTheDocument();
  });

  it('uses the generic message when the server gives none', async () => {
    serve();
    server.use(antiforgeryHandler(), http.post('/api/me/api-key', () => new HttpResponse(null, { status: 500 })));
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />);
    await user.click(await screen.findByRole('button', { name: 'Generate Key' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Generate Key' }));
    expect(await screen.findByText('Failed to generate API key')).toBeInTheDocument();
  });

  it('copies the key to the clipboard, shows Copied!, then reverts', async () => {
    serve('copy-me');
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithProviders(<SettingsPage />);
    const button = await screen.findByRole('button', { name: /Copy/ });
    vi.useFakeTimers({ shouldAdvanceTime: true });
    await user.click(button);

    expect(await screen.findByRole('button', { name: /Copied!/ })).toBeInTheDocument();
    await expect(navigator.clipboard.readText()).resolves.toBe('copy-me');
    act(() => {
      vi.advanceTimersByTime(COPIED_RESET_DELAY_MS);
    });
    expect(await screen.findByRole('button', { name: /^Copy$/ })).toBeInTheDocument();
    vi.useRealTimers();
  });

  it('logs and survives a clipboard failure', async () => {
    serve('copy-me');
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const user = userEvent.setup();
    renderWithProviders(<SettingsPage />);
    const button = await screen.findByRole('button', { name: /Copy/ });
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    await user.click(button);

    await waitFor(() => expect(error).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: /^Copy$/ })).toBeInTheDocument();
  });

  it('shows an error when the settings cannot be loaded', async () => {
    server.use(http.get('/api/me/settings', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<SettingsPage />);
    expect(await screen.findByText("Couldn't load your settings. Try again.")).toBeInTheDocument();
  });
});
