import { Alert, Button, Center, Loader, Stack } from '@mantine/core';
import { Navigate, useLocation } from 'react-router-dom';
import { ApiError } from '../api/client';
import { loginUrl } from '../constants';
import { useMe } from '../hooks/useMe';
import AppShell from './AppShell';

/** Route guard for authenticated pages: loader while api/me resolves, login redirect on 401, else the shell. */
export default function RequireAuth() {
  const { data: me, error, isPending, refetch } = useMe();
  const location = useLocation();

  if (isPending) {
    return (
      <Center mih="100vh">
        <Loader color="gray" aria-label="Loading" />
      </Center>
    );
  }
  if (error instanceof ApiError && error.status === 401) {
    return <Navigate to={loginUrl(location.pathname + location.search)} replace />;
  }
  if (!me) {
    return (
      <Center mih="100vh">
        <Stack align="center">
          <Alert color="red">Couldn't load your account. Try again.</Alert>
          <Button variant="default" onClick={() => void refetch()}>
            Retry
          </Button>
        </Stack>
      </Center>
    );
  }
  return <AppShell me={me} />;
}
