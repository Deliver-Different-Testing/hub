import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Group, Loader, Center, Table, Text, TextInput, Title, Modal } from '@mantine/core';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, Copy, KeyRound } from 'lucide-react';
import { getApiErrorMessage } from '../api/client';
import { generateApiKey, getSettings } from '../api/hub';
import { DialogFooter } from '../components/dialog/DialogFooter';
import { DialogHeader } from '../components/dialog/DialogHeader';
import { DialogShell } from '../components/dialog/DialogShell';
import Icon from '../components/Icon';
import { usePageTitle } from '../hooks/usePageTitle';

export const COPIED_RESET_DELAY_MS = 2000;
const API_KEY_ERROR = 'Failed to generate API key';
const API_KEY_SETTING = 'APIKey';

export default function SettingsPage() {
  usePageTitle('Settings');
  const queryClient = useQueryClient();
  const { data, isPending, isError } = useQuery({ queryKey: ['settings'], queryFn: getSettings, retry: false });
  const [confirming, setConfirming] = useState(false);
  const [generatedKey, setGeneratedKey] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [copied, setCopied] = useState(false);
  const copyTimer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(copyTimer.current), []);

  const generate = useMutation({
    mutationFn: generateApiKey,
    onSuccess: (r) => {
      setGeneratedKey(r.apiKey);
      setError('');
      setConfirming(false);
      void queryClient.invalidateQueries({ queryKey: ['settings'] });
    },
    onError: (e) => {
      setError(getApiErrorMessage(e, API_KEY_ERROR));
      setConfirming(false);
    },
  });

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(apiKey);
      setCopied(true);
      window.clearTimeout(copyTimer.current);
      copyTimer.current = window.setTimeout(() => setCopied(false), COPIED_RESET_DELAY_MS);
    } catch (e) {
      console.error('Failed to copy text:', e);
    }
  };

  if (isPending) {
    return (
      <Center py="xl">
        <Loader aria-label="Loading settings" />
      </Center>
    );
  }
  if (isError) return <Alert color="red">Couldn't load your settings. Try again.</Alert>;

  const apiKey = generatedKey ?? data.find((s) => s.name === API_KEY_SETTING)?.value ?? '';
  const others = data.filter((s) => s.name !== API_KEY_SETTING);

  return (
    <div>
      <Title order={2} mb="md">
        User Settings
      </Title>

      <Card mb="lg">
        <Title order={3} mb="sm">
          API Key
        </Title>
        <Group align="flex-end" wrap="nowrap">
          <TextInput
            id="ApiKey"
            label="API Key"
            placeholder="API Key"
            readOnly
            value={apiKey}
            error={error || undefined}
            style={{ flex: 1 }}
          />
          {apiKey ? (
            <Button
              variant="default"
              title="Copy to clipboard"
              leftSection={<Icon icon={copied ? Check : Copy} size={16} />}
              onClick={() => void copy()}
            >
              {copied ? 'Copied!' : 'Copy'}
            </Button>
          ) : (
            <Button variant="default" onClick={() => setConfirming(true)}>
              Generate Key
            </Button>
          )}
        </Group>
      </Card>

      <Card>
        <Title order={3} mb="sm">
          Other Settings
        </Title>
        <Table highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Setting</Table.Th>
              <Table.Th>Value</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {others.map((s) => (
              <Table.Tr key={s.id}>
                <Table.Td>{s.name}</Table.Td>
                <Table.Td>{s.value}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Card>

      <DialogShell opened={confirming} onClose={() => setConfirming(false)} label="Generate API key" size={480}>
        <DialogHeader
          icon={<Icon icon={KeyRound} size={22} />}
          title={<Modal.Title>Generate API key</Modal.Title>}
          onClose={() => setConfirming(false)}
        />
        <Text p="lg">
          Generate an API key for your account? You can copy it once it has been created.
        </Text>
        <DialogFooter
          onCancel={() => setConfirming(false)}
          submitLabel="Generate Key"
          onSubmit={() => generate.mutate()}
          isSubmitting={generate.isPending}
        />
      </DialogShell>
    </div>
  );
}
