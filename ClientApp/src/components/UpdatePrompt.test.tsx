import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '../test/utils';
import { notifications } from '@mantine/notifications';
import UpdatePrompt, { UPDATE_CHECK_MS, UPDATE_NOTIFICATION_ID } from './UpdatePrompt';

const sw = vi.hoisted(() => ({
  needRefresh: false,
  options: undefined as { onRegisteredSW?: (url: string, registration?: ServiceWorkerRegistration) => void } | undefined,
  setNeedRefresh: vi.fn(),
  updateServiceWorker: vi.fn().mockResolvedValue(undefined),
}));

vi.mock('virtual:pwa-register/react', () => ({
  useRegisterSW: (options?: typeof sw.options) => {
    sw.options = options;
    return {
    needRefresh: [sw.needRefresh, sw.setNeedRefresh],
    offlineReady: [false, vi.fn()],
    updateServiceWorker: sw.updateServiceWorker,
    };
  },
}));

beforeEach(() => {
  sw.needRefresh = false;
  sw.options = undefined;
});

afterEach(() => {
  vi.useRealTimers();
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

  it('checks for a new version every hour, so a tab left open still hears about a release', () => {
    vi.useFakeTimers();
    const registration = { update: vi.fn().mockResolvedValue(undefined) } as unknown as ServiceWorkerRegistration;
    renderWithProviders(<UpdatePrompt />);
    sw.options?.onRegisteredSW?.('/sw.js', registration);

    expect(UPDATE_CHECK_MS).toBe(60 * 60 * 1000);
    vi.advanceTimersByTime(UPDATE_CHECK_MS - 1);
    expect(registration.update).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(registration.update).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(UPDATE_CHECK_MS);
    expect(registration.update).toHaveBeenCalledTimes(2);
  });

  it('copes with no registration (e.g. service workers unavailable)', () => {
    renderWithProviders(<UpdatePrompt />);
    expect(() => sw.options?.onRegisteredSW?.('/sw.js', undefined)).not.toThrow();
  });

  it('hides the notification when unmounted', async () => {
    sw.needRefresh = true;
    const { unmount } = renderWithProviders(<UpdatePrompt />);
    await screen.findByText('Update available');
    const hide = vi.spyOn(notifications, 'hide');
    unmount();
    expect(hide).toHaveBeenCalledWith(UPDATE_NOTIFICATION_ID);
  });
});
