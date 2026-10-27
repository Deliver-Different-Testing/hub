import type { IconProps } from './Icon';
import { Button } from '@mantine/core';
import Icon from './Icon';

interface SubmitButtonProps {
  icon: IconProps['icon'];
  label: string;
  busyLabel: string;
  busy: boolean;
  id?: string;
  fullWidth?: boolean;
}

/**
 * The label lives in its own `.button-label` span so swapping "Log in" for "Logging in..." never
 * clobbers the leading icon.
 */
export default function SubmitButton({ icon, label, busyLabel, busy, id, fullWidth }: SubmitButtonProps) {
  return (
    <Button id={id} type="submit" disabled={busy} fullWidth={fullWidth} leftSection={<Icon icon={icon} size={18} />}>
      <span className="button-label">{busy ? busyLabel : label}</span>
    </Button>
  );
}
