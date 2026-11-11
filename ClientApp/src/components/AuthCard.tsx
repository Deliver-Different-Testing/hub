import type { ReactNode } from 'react';
import { Center, Paper } from '@mantine/core';
import field from '../styles/authBackground.module.css';
import classes from './auth.module.css';

/** Centred card on the Ink Blue login field, used by the anonymous pages (login, forgot and reset password). */
export default function AuthCard({
  children,
  busy,
  busyLabel = 'Signing in',
}: {
  children: ReactNode;
  busy?: boolean;
  /** What the progress bar announces while busy. */
  busyLabel?: string;
}) {
  return (
    <Center component="main" mih="100vh" p="md" className={field.field} data-testid="auth-field">
      <Paper w="100%" maw={440} p="xl" pos="relative" style={{ overflow: 'hidden' }}>
        {busy !== undefined && (
          <div
            id="loginProgress"
            role="progressbar"
            aria-label={busyLabel}
            aria-hidden={!busy}
            className={`${classes.progress} ${busy ? classes.active : ''}`}
          >
            <div className={classes.bar} />
          </div>
        )}
        {children}
      </Paper>
    </Center>
  );
}
