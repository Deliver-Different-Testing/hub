import { useState } from 'react';
import { Button, Group, Menu, Text, useMantineTheme } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useMutation } from '@tanstack/react-query';
import { Check, ChevronDown } from 'lucide-react';
import { getApiErrorMessage } from '../api/client';
import { updateCurrentTenant } from '../api/hub';
import type { Me } from '../api/types';
import { navigateTo, reloadPage } from '../lib/navigate';
import Icon from './Icon';

const SWITCH_ERROR = 'Could not switch tenant. Please try again.';
const ERROR_DISMISS_MS = 8000;

export default function OrgSwitcher({ me }: { me: Me }) {
  const { shell } = useMantineTheme().other;
  const [pendingName, setPendingName] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: updateCurrentTenant,
    onSuccess: (data) => {
      // A redirectUrl means the destination app validates a short-lived token and issues a fresh cookie;
      // otherwise reload in place so every query refetches for the new tenant.
      if (data.redirectUrl) navigateTo(data.redirectUrl);
      else reloadPage();
    },
    onError: (error) => {
      setPendingName(null);
      notifications.show({ color: 'red', autoClose: ERROR_DISMISS_MS, message: getApiErrorMessage(error, SWITCH_ERROR) });
    },
  });

  if (me.tenants.length === 0) return null;
  const currentName = me.tenants.find((t) => t.id === me.currentTenant?.id)?.name ?? 'Select Tenant';

  return (
    <Group gap="xs" wrap="nowrap">
      <Text size="sm" visibleFrom="sm" style={{ color: shell.textSecondary }}>
        Organizations:
      </Text>
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          <Button
            id="tenantDropdown"
            size="sm"
            loading={mutation.isPending}
            disabled={mutation.isPending || pendingName !== null}
            rightSection={<Icon icon={ChevronDown} size={16} />}
          >
            {pendingName ?? currentName}
          </Button>
        </Menu.Target>
        <Menu.Dropdown>
          {me.tenants.map((tenant) => {
            const active = tenant.id === me.currentTenant?.id;
            return (
              <Menu.Item
                key={tenant.id}
                data-tenant-id={tenant.id}
                data-tenant-code={tenant.code}
                aria-current={active}
                rightSection={active ? <Icon icon={Check} size={16} /> : undefined}
                onClick={() => {
                  setPendingName(tenant.name);
                  mutation.mutate(tenant.id);
                }}
              >
                {tenant.name}
              </Menu.Item>
            );
          })}
        </Menu.Dropdown>
      </Menu>
    </Group>
  );
}
