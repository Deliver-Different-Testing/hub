import type { ReactNode } from 'react';
import { Center, Paper } from '@mantine/core';
import field from '../styles/authBackground.module.css';
import classes from './auth.module.css';

/** Centred card on the Ink Blue login field, used by the anonymous pages (login, forgot and reset password). */
export default function AuthCard({ children, busy }: { children: ReactNode; busy?: boolean }) {
  return (
    <Center mih="100vh" p="md" className={field.field} data-testid="auth-field">
      <Paper w="100%" maw={440} p="xl" pos="relative" style={{ overflow: 'hidden' }}>
        {busy !== undefined && (
          <div
            id="loginProgress"
            role="progressbar"
            aria-label="Signing in"
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
