import { ActionIcon, Avatar, Group, Menu, Stack, Text, useMantineTheme } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useMutation } from '@tanstack/react-query';
import { LogOut, Settings } from 'lucide-react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../api/client';
import { logout } from '../api/hub';
import type { Me } from '../api/types';
import { loginUrl } from '../constants';
import { displayNameFromEmail } from '../lib/format';
import { navigateTo } from '../lib/navigate';
import { appBarIconProps } from './appBarChrome';
import Icon from './Icon';

export default function ProfileMenu({ me }: { me: Me }) {
  const { scrim } = useMantineTheme().other;
  const label = me.displayName?.trim() || displayNameFromEmail(me.email);

  const signOut = useMutation({
    mutationFn: logout,
    onSuccess: (data) => navigateTo(data.redirectUrl ?? loginUrl()),
    onError: (error) => notifications.show({ color: 'red', message: getApiErrorMessage(error) }),
  });

  return (
    <Menu position="bottom-end" width={260}>
      <Menu.Target>
        <ActionIcon {...appBarIconProps} aria-label="Account menu" title={label}>
          <Avatar
            name={label}
            size={30}
            radius="xl"
            styles={{ placeholder: { backgroundColor: scrim.fill, color: scrim.text, fontSize: 12, fontWeight: 600 } }}
          />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Group gap="sm" wrap="nowrap" px="sm" py="xs">
          <Avatar name={label} color="brand" variant="filled" radius="xl" />
          <Stack gap={0} miw={0}>
            <Text size="sm" fw={600} truncate data-testid="account-name">
              {label}
            </Text>
            <Text size="xs" c="dimmed" truncate title={me.email}>
              {me.email}
            </Text>
          </Stack>
        </Group>
        <Menu.Divider />
        {/* "User Settings", not "Settings": the hub tile that opens the configurator is called Settings. */}
        <Menu.Item component={Link} to="/Account/Settings" leftSection={<Icon icon={Settings} size={16} />}>
          User Settings
        </Menu.Item>
        <Menu.Item
          leftSection={<Icon icon={LogOut} size={16} />}
          disabled={signOut.isPending}
          onClick={() => signOut.mutate()}
        >
          Logout
        </Menu.Item>
      </Menu.Dropdown>
    </Menu>
  );
}
