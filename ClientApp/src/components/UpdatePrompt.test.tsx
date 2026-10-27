import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '../test/utils';
import UpdatePrompt from './UpdatePrompt';

const sw = vi.hoisted(() => ({
  needRefresh: false,
  setNeedRefresh: vi.fn(),
  updateServiceWorker: vi.fn().mockResolvedValue(undefined),
}));

vi.mock('virtual:pwa-register/react', () => ({
  useRegisterSW: () => ({
    needRefresh: [sw.needRefresh, sw.setNeedRefresh],
    offlineReady: [false, vi.fn()],
    updateServiceWorker: sw.updateServiceWorker,
  }),
}));

beforeEach(() => {
  sw.needRefresh = false;
});

describe('UpdatePrompt', () => {
  it('shows nothing while the app is up to date', () => {
    renderWithProviders(<UpdatePrompt />);
    expect(screen.queryByText('Update available')).not.toBeInTheDocument();
  });

  it('shows a notification with a Reload button when a new version is waiting', async () => {
    sw.needRefresh = true;
    renderWithProviders(<UpdatePrompt />);
    expect(await screen.findByText('Update available')).toBeInTheDocument();
    expect(screen.getByText('A new version of Hub is ready.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
  });

  it('activates the waiting service worker and reloads when Reload is pressed', async () => {
    sw.needRefresh = true;
    const user = userEvent.setup();
    renderWithProviders(<UpdatePrompt />);
    await user.click(await screen.findByRole('button', { name: 'Reload' }));
    expect(sw.updateServiceWorker).toHaveBeenCalledWith(true);
  });

  it('clears the needRefresh flag when the notification is dismissed', async () => {
    sw.needRefresh = true;
    const user = userEvent.setup();
    renderWithProviders(<UpdatePrompt />);
    await screen.findByText('Update available');
    await user.click(document.querySelector<HTMLElement>('.mantine-Notification-closeButton')!);
    await vi.waitFor(() => expect(sw.setNeedRefresh).toHaveBeenCalledWith(false));
  });
});
