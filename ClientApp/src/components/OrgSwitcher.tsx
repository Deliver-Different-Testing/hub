import { useMemo, useState } from 'react';
import type { ComponentProps } from 'react';
import { Avatar, Combobox, Group, Loader, ScrollArea, Stack, Text, UnstyledButton, useCombobox, useMantineTheme } from '@mantine/core';
import { notifications } from '@mantine/notifications';
import { useMutation } from '@tanstack/react-query';
import {
  Briefcase,
  Building,
  Building2,
  Check,
  ChevronDown,
  Container,
  Factory,
  Globe,
  Hotel,
  Landmark,
  Package,
  Store,
  Truck,
  Warehouse,
} from 'lucide-react';
import { getApiErrorMessage } from '../api/client';
import { updateCurrentTenant } from '../api/hub';
import type { Me, Tenant } from '../api/types';
import { navigateTo, reloadPage } from '../lib/navigate';
import Icon from './Icon';
import classes from './appBar.module.css';
import menu from './orgSwitcher.module.css';

const SWITCH_ERROR = 'Could not switch organization. Please try again.';
const ERROR_DISMISS_MS = 8000;
// Past this many organizations a filter box earns its place; below it, it is just one more thing to read.
const SEARCH_THRESHOLD = 7;

const byName = (a: Tenant, b: Tenant) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' });

type Glyph = ComponentProps<typeof Icon>['icon'];

// Each organization in the list gets its own company glyph, handed out in list order so no two share one
// until the set runs out (then it repeats).
const ORG_ICONS: Glyph[] = [Building2, Warehouse, Factory, Store, Landmark, Building, Hotel, Truck, Container, Package, Briefcase, Globe];

// A rounded-square tile, so an organization never reads as the round user avatar beside it.
function OrgAvatar({ icon, size, onShell }: { icon: Glyph; size: number; onShell?: boolean }) {
  const primary = useMantineTheme().primaryColor;
  return (
    <Avatar
      size={size}
      radius="sm"
      variant="light"
      color={onShell ? undefined : primary}
      classNames={onShell ? { placeholder: classes.avatar } : undefined}
      aria-hidden
    >
      <Icon icon={icon} size={Math.round(size * 0.6)} stroke={1.75} />
    </Avatar>
  );
}

export default function OrgSwitcher({ me }: { me: Me }) {
  const [pendingName, setPendingName] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const tenants = useMemo(() => [...me.tenants].sort(byName), [me.tenants]);
  const iconFor = (tenant: Tenant | undefined) =>
    ORG_ICONS[Math.max(0, tenants.indexOf(tenant as Tenant)) % ORG_ICONS.length];
  const withSearch = tenants.length > SEARCH_THRESHOLD;

  const combobox = useCombobox({
    onDropdownOpen: () => {
      if (withSearch) combobox.focusSearchInput();
      // Open on the current organization, so Enter without moving is a no-op rather than a switch.
      else combobox.selectActiveOption();
    },
    onDropdownClose: () => {
      combobox.resetSelectedOption();
      setSearch('');
    },
  });

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

  if (tenants.length === 0) return null;
  const current = tenants.find((t) => t.id === me.currentTenant?.id);
  const currentName = current?.name ?? 'Select organization';

  // One organization is nothing to switch between: show where the user is, without a one-item dropdown.
  if (tenants.length === 1 && current) {
    return (
      <Group gap="xs" wrap="nowrap" miw={0} className={classes.tenantStatic}>
        <OrgAvatar icon={iconFor(current)} size={24} onShell />
        <span className={classes.tenantChipLabel}>{current.name}</span>
      </Group>
    );
  }

  const switching = mutation.isPending || pendingName !== null;
  const shown = tenants.find((t) => t.name === pendingName) ?? current;
  const query = search.trim().toLowerCase();
  const visible = query
    ? tenants.filter((t) => t.name.toLowerCase().includes(query) || t.code.toLowerCase().includes(query))
    : tenants;

  return (
    <Combobox
      store={combobox}
      position="bottom-end"
      width={260}
      radius="sm"
      shadow="md"
      withinPortal
      classNames={{ dropdown: menu.orgDropdown, option: menu.option }}
      onOptionSubmit={(value) => {
        combobox.closeDropdown();
        const tenant = tenants.find((t) => String(t.id) === value);
        if (!tenant || tenant.id === current?.id) return;
        setPendingName(tenant.name);
        mutation.mutate(tenant.id);
      }}
    >
      <Combobox.Target targetType="button">
        {/* The visible name leads the accessible name so voice control can target it (WCAG 2.5.3). */}
        <UnstyledButton
          id="tenantDropdown"
          aria-label={switching ? `Switching to ${pendingName}` : `${currentName}, switch organization`}
          aria-busy={switching}
          disabled={switching}
          className={`${classes.tenantChip} ${classes.focusRing}`}
          onClick={() => combobox.toggleDropdown()}
        >
          <OrgAvatar icon={iconFor(shown)} size={24} onShell />
          <span className={classes.tenantChipLabel}>{pendingName ?? currentName}</span>
          {switching ? (
            <Loader size={12} color="currentColor" />
          ) : (
            <span className={classes.caret} aria-hidden>
              <Icon icon={ChevronDown} size={14} />
            </span>
          )}
        </UnstyledButton>
      </Combobox.Target>

      <Combobox.Dropdown>
        {current && (
          <>
            <Group gap="sm" wrap="nowrap" className={menu.header} data-testid="org-switcher-header">
              <OrgAvatar icon={iconFor(current)} size={36} />
              <Stack gap={0} miw={0}>
                <Text size="sm" fw={600} c="var(--mantine-color-text)" truncate>
                  {current.name}
                </Text>
                <Text size="xs" c="dimmed">
                  Current organization
                </Text>
              </Stack>
            </Group>
            <div className={menu.divider} />
          </>
        )}
        {withSearch && (
          <Combobox.Search
            value={search}
            onChange={(e) => {
              setSearch(e.currentTarget.value);
              combobox.updateSelectedOptionIndex();
            }}
            placeholder="Search organizations"
            aria-label="Search organizations"
          />
        )}
        <Combobox.Options aria-label="Organizations">
          <ScrollArea.Autosize mah={320} type="scroll">
            {visible.length === 0 ? (
              <Combobox.Empty>No organizations found</Combobox.Empty>
            ) : (
              visible.map((tenant) => {
                const active = tenant.id === current?.id;
                return (
                  <Combobox.Option
                    key={tenant.id}
                    value={String(tenant.id)}
                    active={active}
                    aria-selected={active}
                    data-tenant-id={tenant.id}
                    data-tenant-code={tenant.code}
                  >
                    <Group gap="sm" wrap="nowrap">
                      <OrgAvatar icon={iconFor(tenant)} size={24} />
                      <Text size="sm" fw={active ? 600 : undefined} truncate style={{ flex: 1 }}>
                        {tenant.name}
                      </Text>
                      {active && <Icon icon={Check} size={16} />}
                    </Group>
                  </Combobox.Option>
                );
              })
            )}
          </ScrollArea.Autosize>
        </Combobox.Options>
      </Combobox.Dropdown>
    </Combobox>
  );
}
