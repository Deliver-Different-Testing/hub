import { useEffect } from 'react';
import { Button, Stack, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useRegisterSW } from 'virtual:pwa-register/react';
import { CircleFadingArrowUp } from 'lucide-react';
import Icon from './Icon';

export const UPDATE_NOTIFICATION_ID = 'pwa-update';
// The browser only re-checks sw.js on navigation, so a tab left open all day would never hear
// about a release without this (same cadence as dfrntcrm's PwaUpdatePrompt).
export const UPDATE_CHECK_MS = 60 * 60 * 1000;

/** Shows a persistent "new version" notification with an Update now button once the service worker has an update waiting. */
export default function UpdatePrompt() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      if (registration) setInterval(() => void registration.update(), UPDATE_CHECK_MS);
    },
  });

  useEffect(() => {
    if (!needRefresh) return;
    // Swap to a spinner rather than closing, so the click visibly does something until the reload lands.
    // The close button stays in case the reload never comes.
    const install = () => {
      notifications.update({ id: UPDATE_NOTIFICATION_ID, message: 'Updating…', loading: true });
      updateServiceWorker(true).catch(() => {
        notifications.update({ id: UPDATE_NOTIFICATION_ID, message: "Couldn't update. Reload the page to try again.", loading: false });
      });
    };
    notifications.show({
      id: UPDATE_NOTIFICATION_ID,
      title: 'Update available',
      icon: <Icon icon={CircleFadingArrowUp} size={18} stroke={2} />,
      autoClose: false,
      onClose: () => setNeedRefresh(false),
      message: (
        <Stack gap="sm" align="flex-start">
          <Text size="sm">A new version of the App Hub is ready. Updating reloads the page, so save any changes first.</Text>
          <Button size="xs" onClick={install}>Update now</Button>
        </Stack>
      ),
    });
    return () => {
      notifications.hide(UPDATE_NOTIFICATION_ID);
    };
  }, [needRefresh, setNeedRefresh, updateServiceWorker]);

  return null;
}
