import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { Alert, Button, Center, Divider, Group, List, Loader, SimpleGrid, Stack, Text, ThemeIcon, Title } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { Check, X } from 'lucide-react';
import { useSearchParams } from 'react-router-dom';
import { getApiErrorMessage } from '../api/client';
import { getAgentAccessConnect } from '../api/hub';
import type { AgentAccessConnectPage as ConnectPage } from '../api/types';
import AuthCard from '../components/AuthCard';
import Icon from '../components/Icon';
import TenantLogo from '../components/TenantLogo';
import { usePageTitle } from '../hooks/usePageTitle';

const NO_TICKET = "Couldn't open this connection request. The link is incomplete. Go back to the app and connect again.";
const START_AGAIN = "Couldn't open this connection request. Go back to the app and connect again.";

/**
 * Where /connect/authorize sends someone to approve an AI app, or to switch to the organisation the app is connecting
 * to. Everything shown comes from the sealed ticket in the URL; the form posts the original authorization request back
 * to /connect/authorize as a top-level navigation, because the answer is a redirect to the app.
 */
export default function AgentAccessConnectPage() {
  const [params] = useSearchParams();
  const ticket = params.get('ticket') ?? '';
  const { data, error, isPending } = useQuery({
    queryKey: ['agent-access-connect', ticket],
    queryFn: () => getAgentAccessConnect(ticket),
    enabled: ticket !== '',
    // An expired, tampered or someone else's ticket answers the same every time.
    retry: false,
    staleTime: Infinity,
    gcTime: 0,
  });
  const [submitting, setSubmitting] = useState(false);
  // Guards the decision itself: a second click would post the request again and could create a second grant. A ref,
  // not disabled buttons, so the clicked button's name/value still travel with the first submission.
  const submitted = useRef(false);
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    if (submitted.current) {
      e.preventDefault();
      return;
    }
    submitted.current = true;
    setSubmitting(true);
  };

  // Back from the app restores this page from the back/forward cache with the form still marked as sent.
  useEffect(() => {
    const onPageShow = (e: PageTransitionEvent) => {
      if (!e.persisted) return;
      submitted.current = false;
      setSubmitting(false);
    };
    window.addEventListener('pageshow', onPageShow);
    return () => window.removeEventListener('pageshow', onPageShow);
  }, []);
  usePageTitle(data ? `Connect ${data.clientName}` : 'Connect an AI app');

  let body: ReactNode;
  if (ticket === '' || error) {
    body = (
      <Alert color="red" role="alert">
        {ticket === '' ? NO_TICKET : getApiErrorMessage(error, START_AGAIN)}
      </Alert>
    );
  } else if (isPending) {
    body = (
      <Center py="xl">
        <Loader color="gray" aria-label="Loading" />
      </Center>
    );
  } else {
    body = (
      <form method="post" action="/connect/authorize" onSubmit={onSubmit}>
        {data.parameters.map((p, i) => (
          <input key={`${p.name}-${i}`} type="hidden" name={p.name} value={p.value} />
        ))}
        <input type="hidden" name={data.antiforgeryFieldName} value={data.antiforgeryToken} />
        {data.kind === 'switch' ? <SwitchOrganisation page={data} /> : <Consent page={data} />}
      </form>
    );
  }

  return (
    <AuthCard busy={submitting} busyLabel="Connecting">
      <Stack gap="lg">
        <Center>
          <TenantLogo height={32} />
        </Center>
        {body}
      </Stack>
    </AuthCard>
  );
}

function Consent({ page }: { page: ConnectPage }) {
  const { clientName } = page;
  const who = page.verifiedVendor
    ? `${clientName} by ${page.verifiedVendor}`
    : page.clientHost
      ? `${clientName} from ${page.clientHost}`
      : clientName;

  return (
    <Stack gap="md">
      <div>
        <Title order={2} ta="center">
          Connect {clientName}
        </Title>
        <Text c="dimmed" ta="center" mt={4}>
          {who} wants to use your DFRNT account on{' '}
          <Text span fw={600} c="var(--mantine-color-text)">
            {page.resourceHost}
          </Text>
          .
        </Text>
      </div>

      <Divider />
      <Scope title={`${clientName} will be able to`} allowed>
        <List.Item>Read anything you can already see in DFRNT.</List.Item>
        <List.Item>Stay connected until you revoke it. After 90 days unused, you'll need to connect again.</List.Item>
      </Scope>
      <Divider />
      <Scope title={`${clientName} won't be able to`}>
        <List.Item>Create, change or delete anything.</List.Item>
        <List.Item>Send messages, take payments or use accounting.</List.Item>
        <List.Item>See your password or log in as you anywhere else.</List.Item>
      </Scope>
      <Divider />

      <Text size="xs" c="dimmed">
        You can revoke this connection in Integration Manager at any time, and your administrator can turn off AI app
        access. {clientName}'s own privacy policy, not DFRNT's, covers what it does with the data it reads.
      </Text>

      <Actions>
        <Button type="submit" name="decision" value="deny" variant="default">
          Don't allow
        </Button>
        <Button type="submit" name="decision" value="allow">
          Allow access
        </Button>
      </Actions>
      {page.redirectHost && (
        <Text size="xs" c="dimmed" ta="center">
          You'll go back to {page.redirectHost} when you choose.
        </Text>
      )}
    </Stack>
  );
}

function SwitchOrganisation({ page }: { page: ConnectPage }) {
  const { clientName, currentOrganisation, hubOrganisation } = page;

  return (
    <Stack gap="md">
      <div>
        <Title order={2} ta="center">
          Connect {clientName}
        </Title>
        <Text c="dimmed" ta="center" mt={4}>
          You're logged in to {currentOrganisation}, but {clientName} is connecting to {hubOrganisation}.
        </Text>
      </div>

      {page.error && (
        <Alert color="red" role="alert">
          {page.error}
        </Alert>
      )}

      <Text size="sm">
        {page.canSwitch
          ? `To continue, switch to ${hubOrganisation}. Hub will switch to it as well.`
          : `Your account doesn't have access to ${hubOrganisation}, so you can't connect ${clientName} to it. Log in with an account that does, or ask your administrator for access.`}
      </Text>

      <Actions single={!page.canSwitch}>
        <Button type="submit" name="decision" value="cancel" variant="default">
          Cancel
        </Button>
        {page.canSwitch && (
          <Button type="submit" name="decision" value="switch">
            Switch to {hubOrganisation}
          </Button>
        )}
      </Actions>
    </Stack>
  );
}

function Scope({ title, allowed = false, children }: { title: string; allowed?: boolean; children: ReactNode }) {
  return (
    <div>
      <Title order={3} fz="sm" fw={600} mb="xs">
        {title}
      </Title>
      <List
        spacing="xs"
        size="sm"
        center
        icon={
          <ThemeIcon size={22} radius="xl" variant="light" color={allowed ? undefined : 'gray'}>
            <Icon icon={allowed ? Check : X} size={14} stroke={2} />
          </ThemeIcon>
        }
      >
        {children}
      </List>
    </div>
  );
}

// Outlined cancel beside the filled action, as DialogFooter lays them out.
function Actions({ single = false, children }: { single?: boolean; children: ReactNode }) {
  return single ? (
    <Group grow>{children}</Group>
  ) : (
    <SimpleGrid cols={2} spacing="sm">
      {children}
    </SimpleGrid>
  );
}
