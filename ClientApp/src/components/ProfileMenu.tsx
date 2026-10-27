import { Group, Menu, Text, UnstyledButton, useMantineTheme } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useMutation } from '@tanstack/react-query';
import { ChevronDown, CircleUserRound, LogOut, Settings } from 'lucide-react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../api/client';
import { logout } from '../api/hub';
import type { Me } from '../api/types';
import { loginUrl } from '../constants';
import { displayNameFromEmail } from '../lib/format';
import { navigateTo } from '../lib/navigate';
import Icon from './Icon';

export default function ProfileMenu({ me }: { me: Me }) {
  const { shell } = useMantineTheme().other;
  const label = me.displayName?.trim() || displayNameFromEmail(me.email);

  const signOut = useMutation({
    mutationFn: logout,
    onSuccess: (data) => navigateTo(data.redirectUrl ?? loginUrl()),
    onError: (error) => notifications.show({ color: 'red', message: getApiErrorMessage(error) }),
  });

  return (
    <Menu position="bottom-end" width={260}>
      <Menu.Target>
        <UnstyledButton aria-label="Account menu" style={{ color: shell.textPrimary }}>
          <Group gap={6} wrap="nowrap">
            <Text size="sm" fw={500} visibleFrom="xs" inherit>
              {label}
            </Text>
            <Icon icon={ChevronDown} size={16} />
          </Group>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label title={me.email}>
          <Group gap="xs" wrap="nowrap">
            <Icon icon={CircleUserRound} size={18} />
            <Text size="sm" truncate inherit>
              {me.email}
            </Text>
          </Group>
        </Menu.Label>
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
