import { Group, Text } from '@mantine/core';
import { formatHeaderDate } from '../lib/format';
import TenantLogo from './TenantLogo';

/** Logo + eyebrow on the left, today's date (tenant wall-clock) on the right. */
export default function PageHeader({ eyebrow, today }: { eyebrow: string; today?: string }) {
  return (
    <Group justify="space-between" align="center" mb="lg" wrap="wrap">
      <Group gap="md" wrap="nowrap">
        <TenantLogo height={32} />
        <Text fw={600} tt="uppercase" fz="sm" c="dimmed" style={{ letterSpacing: '0.08em' }}>
          {eyebrow}
        </Text>
      </Group>
      {today && <Text c="dimmed">{formatHeaderDate(today)}</Text>}
    </Group>
  );
}
