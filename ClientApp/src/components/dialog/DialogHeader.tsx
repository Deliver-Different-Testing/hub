import type { ReactNode } from 'react';
import { Box, CloseButton, Group, Text, ThemeIcon } from '@mantine/core';
import { dialogChipProps, dialogKeyline, type DialogVariant } from './dialogStyles';
import classes from './DialogHeader.module.css';

export interface DialogHeaderProps {
  /** A plain icon element, sized by the caller (about 22px); the chip centres it. */
  icon?: ReactNode;
  /** Picks the chip accent. `primary` follows the tenant brand. */
  variant?: DialogVariant;
  /** CSS colour overriding the variant's accent. */
  iconColor?: string;
  /** Pass `<Modal.Title>` so the dialog is labelled by it. */
  title: ReactNode;
  subtitle?: string;
  onClose?: () => void;
  closeDisabled?: boolean;
  /** Extra controls rendered before the close button. */
  actions?: ReactNode;
}

/** Neutral bar + keyline; the icon chip is the only coloured part. */
export function DialogHeader({
  icon, variant = 'primary', iconColor, title, subtitle, onClose, closeDisabled = false, actions,
}: DialogHeaderProps) {
  return (
    <Group
      gap="md"
      wrap="nowrap"
      px="lg"
      py="sm"
      style={{ backgroundColor: 'var(--dd-dialog-bar)', borderBottom: dialogKeyline }}
    >
      {icon && (
        <ThemeIcon aria-hidden {...dialogChipProps(variant, iconColor)}>
          {icon}
        </ThemeIcon>
      )}
      <Box style={{ flex: 1, minWidth: 0 }}>
        <Text component="div" fw={600} fz="lg" lh={1.3}>{title}</Text>
        {subtitle && <Text size="sm" c="dimmed" mt={2} truncate>{subtitle}</Text>}
      </Box>
      {actions}
      {onClose && (
        <CloseButton
          onClick={onClose}
          disabled={closeDisabled}
          aria-label="Close"
          iconSize={20}
          className={classes.closeButton}
        />
      )}
    </Group>
  );
}
