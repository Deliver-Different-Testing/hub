import { Center, Loader, Stack, Text, Title } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { ApiError } from '../api/client';
import { getAgentAccessUnavailable } from '../api/hub';
import AuthCard from '../components/AuthCard';
import { usePageTitle } from '../hooks/usePageTitle';

const FAILED = "Couldn't check AI app access. Close this window and connect the app again.";

/** Where the endpoint gate sends an AI app's sign-in window while the organisation has AI app access switched off. */
export default function AgentAccessUnavailablePage() {
  usePageTitle('AI app access');
  const { data, error, isPending } = useQuery({
    queryKey: ['agent-access-unavailable'],
    queryFn: getAgentAccessUnavailable,
    retry: false,
    gcTime: 0,
  });

  // 404 means agent access is dark: Hub must look as if it had no OAuth server, so there is no page to show.
  // Nothing about AI app access shows until the server has answered.
  if (error instanceof ApiError && error.status === 404) return <Navigate to="/" replace />;
  if (isPending) {
    return (
      <Center mih="100vh">
        <Loader color="gray" aria-label="Loading" />
      </Center>
    );
  }

  return (
    <AuthCard>
      <Stack gap="sm" ta="center">
        <Title order={2}>AI app access</Title>
        <Text c="dimmed" mih="3em">
          {data?.message ?? (error ? FAILED : null)}
        </Text>
        <Text size="sm" c="dimmed">
          You can close this window.
        </Text>
      </Stack>
    </AuthCard>
  );
}
