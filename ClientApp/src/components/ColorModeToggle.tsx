import { useState } from 'react';
import { ActionIcon, Menu, Tooltip, useMantineTheme } from '@mantine/core';
import { Check, Moon, Sun, SunMoon } from 'lucide-react';
import { useColorMode, type ColorModePreference } from '../hooks/useColorMode';
import Icon from './Icon';
import { appBarIconProps } from './appBarChrome';

const OPTIONS: { value: ColorModePreference; label: string; icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
  { value: 'system', label: 'System', icon: SunMoon },
];

export default function ColorModeToggle() {
  const { preference, setPreference } = useColorMode();
  const { scrim } = useMantineTheme().other;
  const [opened, setOpened] = useState(false);
  const current = OPTIONS.find((o) => o.value === preference) ?? OPTIONS[2];

  return (
    <Menu position="bottom-end" width={180} opened={opened} onChange={setOpened}>
      <Tooltip label="Change theme" position="bottom" withArrow openDelay={200} disabled={opened}>
        <Menu.Target>
          <ActionIcon
            {...appBarIconProps}
            aria-label="Change theme"
            style={{ color: scrim.text }}
          >
            <Icon icon={current.icon} size={18} />
          </ActionIcon>
        </Menu.Target>
      </Tooltip>
      <Menu.Dropdown>
        {OPTIONS.map(({ value, label, icon }) => (
          <Menu.Item
            key={value}
            aria-current={preference === value}
            leftSection={<Icon icon={icon} size={16} />}
            rightSection={preference === value ? <Icon icon={Check} size={16} /> : undefined}
            onClick={() => setPreference(value)}
          >
            {label}
          </Menu.Item>
        ))}
      </Menu.Dropdown>
    </Menu>
  );
}
