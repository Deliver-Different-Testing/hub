import type { ReactNode } from 'react';
import { Button, Group } from '@mantine/core';
import { dialogKeyline } from './dialogStyles';
import classes from './DialogHeader.module.css';

export type DialogTone = 'default' | 'danger';

export interface DialogFooterProps {
  cancelLabel?: string;
  onCancel: () => void;
  cancelDisabled?: boolean;
  /** Omit to render a custom primary action via `children`. */
  submitLabel?: string;
  onSubmit?: () => void;
  /** `'submit'` for a form footer: the primary button submits the enclosing <form>. */
  submitType?: 'submit' | 'button';
  submitDisabled?: boolean;
  isSubmitting?: boolean;
  tone?: DialogTone;
  /** Start focus on Cancel (confirmations); forms focus their first field instead. */
  focusCancel?: boolean;
  align?: 'flex-end' | 'center';
  /** Draw the bar surface + top keyline (the default). Off when nested inside padded content. */
  bar?: boolean;
  children?: ReactNode;
}

/** Cancel (outlined) then the filled primary action, on the bar surface under a keyline. */
export function DialogFooter({
  cancelLabel = 'Cancel', onCancel, cancelDisabled, submitLabel, onSubmit, submitType = 'button',
  submitDisabled, isSubmitting, tone = 'default', focusCancel, align = 'flex-end', bar = true, children,
}: DialogFooterProps) {
  return (
    <Group
      justify={align}
      gap="sm"
      px={bar ? 'lg' : 0}
      py={bar ? 'md' : 'xs'}
      className={classes.footer}
      style={bar ? { backgroundColor: 'var(--dd-dialog-bar)', borderTop: dialogKeyline } : undefined}
    >
      <Button variant="default" miw={100} onClick={onCancel} disabled={cancelDisabled} data-autofocus={focusCancel ? true : undefined}>
        {cancelLabel}
      </Button>
      {children}
      {submitLabel && (
        <Button
          miw={100}
          type={submitType}
          onClick={onSubmit}
          color={tone === 'danger' ? 'red' : undefined}
          data-tone={tone}
          loading={isSubmitting}
          disabled={submitDisabled}
        >
          {submitLabel}
        </Button>
      )}
    </Group>
  );
}
