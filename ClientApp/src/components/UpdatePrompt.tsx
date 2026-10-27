import { useEffect } from 'react';
import { Button, Group, Text } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useRegisterSW } from 'virtual:pwa-register/react';

export const UPDATE_NOTIFICATION_ID = 'pwa-update';

/** Shows a persistent "new version" notification with a Reload button once the service worker has an update waiting. */
export default function UpdatePrompt() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW();

  useEffect(() => {
    if (!needRefresh) return;
    notifications.show({
      id: UPDATE_NOTIFICATION_ID,
      title: 'Update available',
      autoClose: false,
      onClose: () => setNeedRefresh(false),
      message: (
        <Group justify="space-between" wrap="nowrap" gap="sm">
          <Text size="sm">A new version of Hub is ready.</Text>
          <Button size="xs" onClick={() => void updateServiceWorker(true)}>
            Reload
          </Button>
        </Group>
      ),
    });
  }, [needRefresh, setNeedRefresh, updateServiceWorker]);

  return null;
}
