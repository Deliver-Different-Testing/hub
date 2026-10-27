import { Children, isValidElement, type ElementType, type FormEvent, type ReactNode } from 'react';
import { Modal, type ModalRootProps } from '@mantine/core';
import { DialogHeader } from './DialogHeader';
import { DialogFooter } from './DialogFooter';
import { DIALOG_SIZES, dialogModalShadow, dialogScrollRegionStyle, dialogShellStyles } from './dialogStyles';

const isChrome = (node: ReactNode, kind: ElementType) => isValidElement(node) && node.type === kind;

/** Splits children into a leading <DialogHeader>, a trailing <DialogFooter> and the body between. */
function splitChrome(children: ReactNode) {
  const items = Children.toArray(children);
  let first = 0;
  while (first < items.length && isChrome(items[first], DialogHeader)) first++;
  let last = items.length;
  while (last > first && isChrome(items[last - 1], DialogFooter)) last--;
  return { header: items.slice(0, first), body: items.slice(first, last), footer: items.slice(last) };
}

export interface DialogShellProps extends Omit<ModalRootProps, 'children' | 'size' | 'onSubmit'> {
  size?: number | string;
  /** Accessible name when the header has no `<Modal.Title>`. */
  label?: string;
  /** Renders header/body/footer inside one <form> (Enter submits) when given. */
  onSubmit?: (e: FormEvent<HTMLFormElement>) => void;
  children: ReactNode;
}

/**
 * The DFRNT dialog frame: header bar / scrolling body / footer bar in one rounded (28px)
 * column. Compose `<DialogShell><DialogHeader/> ...body... <DialogFooter/></DialogShell>`;
 * only the body scrolls. Built from `Modal.Root` so `label` reaches the `role="dialog"` node.
 */
export function DialogShell({ size = DIALOG_SIZES.md, label, onSubmit, children, styles, ...rest }: DialogShellProps) {
  const { header, body, footer } = splitChrome(children);
  // Merged per slot so a caller overriding one property of `content` keeps the column layout.
  const slotStyles = typeof styles === 'function' ? styles : {
    ...styles,
    content: { ...dialogShellStyles.content, ...styles?.content },
    body: { ...dialogShellStyles.body, ...styles?.body },
  };
  const bands = (
    <>
      {header}
      <div data-dialog-scroll style={dialogScrollRegionStyle}>{body}</div>
      {footer}
    </>
  );

  return (
    <Modal.Root size={size} centered padding={0} radius="xl" shadow={dialogModalShadow} {...rest} styles={slotStyles}>
      <Modal.Overlay backgroundOpacity={0.32} />
      <Modal.Content aria-label={label}>
        <Modal.Body>
          {onSubmit ? (
            <form
              noValidate
              onSubmit={onSubmit}
              style={{ display: 'flex', flexDirection: 'column', flex: '1 1 auto', minHeight: 0 }}
            >
              {bands}
            </form>
          ) : bands}
        </Modal.Body>
      </Modal.Content>
    </Modal.Root>
  );
}
