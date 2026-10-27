import { useState } from 'react';
import type { CSSProperties } from 'react';
import { Button, Menu, Tooltip, useMantineTheme } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useMutation } from '@tanstack/react-query';
import { Building2, Check, ChevronDown } from 'lucide-react';
import { getApiErrorMessage } from '../api/client';
import { updateCurrentTenant } from '../api/hub';
import type { Me } from '../api/types';
import { navigateTo, reloadPage } from '../lib/navigate';
import Icon from './Icon';
import classes from './growOnHover.module.css';

const SWITCH_ERROR = 'Could not switch tenant. Please try again.';
const ERROR_DISMISS_MS = 8000;

export default function OrgSwitcher({ me }: { me: Me }) {
  const { scrim } = useMantineTheme().other;
  const [opened, setOpened] = useState(false);
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
    <Menu position="bottom-end" withinPortal opened={opened} onChange={setOpened}>
      <Tooltip label="Switch organization" position="bottom" withArrow openDelay={300} disabled={opened}>
        <Menu.Target>
          <Button
            id="tenantDropdown"
            variant="subtle"
            size="sm"
            classNames={{ root: classes.shellButton, inner: classes.shellButtonInner, label: classes.shellButtonLabel }}
            loading={mutation.isPending}
            disabled={mutation.isPending || pendingName !== null}
            leftSection={<Icon icon={Building2} size={16} />}
            rightSection={<Icon icon={ChevronDown} size={14} />}
            style={{ color: scrim.text, '--app-bar-hover': scrim.hoverFill } as CSSProperties}
          >
            {pendingName ?? currentName}
          </Button>
        </Menu.Target>
      </Tooltip>
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
  );
}
