import { ActionIcon, Menu } from '@mantine/core';
import { useColorScheme } from '@mantine/hooks';
import { Check, Moon, Sun } from 'lucide-react';
import { useColorMode, type ColorModePreference } from '../hooks/useColorMode';
import Icon from './Icon';
import { appBarIconProps } from './appBarChrome';

const OPTIONS: { value: ColorModePreference; label: string; icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
  { value: 'system', label: 'System', icon: Sun },
];

/** The light / dark / system picker in the app bar, as dfrntcrm's ThemeMenu: the button shows the theme in use. */
export default function ColorModeToggle() {
  const { mode, preference, setPreference } = useColorMode();
  // System has no glyph of its own: it shows whichever theme the device is on.
  const systemIcon = useColorScheme() === 'dark' ? Moon : Sun;

  return (
    <Menu position="bottom-end" width={180}>
      <Menu.Target>
        <ActionIcon {...appBarIconProps} aria-label="Theme">
          <Icon icon={mode === 'dark' ? Moon : Sun} size={18} />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        {OPTIONS.map(({ value, label, icon }) => (
          <Menu.Item
            key={value}
            aria-current={preference === value}
            leftSection={<Icon icon={value === 'system' ? systemIcon : icon} size={16} />}
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
