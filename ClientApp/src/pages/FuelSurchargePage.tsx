import { useMemo, useState } from 'react';
import { Alert, Badge, Card, Center, Group, Loader, Pagination, SimpleGrid, Table, Text, Title, UnstyledButton } from '@mantine/core';
import { IconGasStation } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { ChevronDown, ChevronUp, ChevronsUpDown } from 'lucide-react';
import { ApiError } from '../api/client';
import { getFuelSurcharge } from '../api/hub';
import type { FuelSurchargeRow } from '../api/types';
import Icon from '../components/Icon';
import PageHeader from '../components/PageHeader';
import { usePageTitle } from '../hooks/usePageTitle';
import { formatPercent, formatPump, formatShortDate } from '../lib/format';

export const PAGE_SIZE = 10;

type SortKey = 'status' | 'scope' | 'rate' | 'pump' | 'start' | 'end';
type Direction = 'ascending' | 'descending';

const COLUMNS: { key: SortKey; label: string; end?: boolean }[] = [
  { key: 'status', label: 'Status' },
  { key: 'scope', label: 'Scope' },
  { key: 'rate', label: 'Rate', end: true },
  { key: 'pump', label: 'Pump price', end: true },
  { key: 'start', label: 'Start' },
  { key: 'end', label: 'End' },
];

const statusOf = (r: FuelSurchargeRow) => (r.isCurrent ? 0 : r.active ? 1 : 2);
const statusLabel = (r: FuelSurchargeRow) => ['Current', 'Active', 'Inactive'][statusOf(r)];
const isoDate = (d: string | null) => (d ? d.slice(0, 10) : '9999-12-31');
const decimal = (n: number | null) => (n == null ? '' : n.toFixed(4));

function sortValue(r: FuelSurchargeRow, key: SortKey): string {
  switch (key) {
    case 'status': return String(statusOf(r));
    case 'scope': return r.scopeLabel;
    case 'rate': return decimal(r.rate);
    case 'pump': return decimal(r.pumpPrice);
    case 'start': return isoDate(r.start);
    case 'end': return isoDate(r.end);
  }
}

function compare(key: SortKey, direction: Direction) {
  const sign = direction === 'descending' ? -1 : 1;
  return (a: FuelSurchargeRow, b: FuelSurchargeRow) => {
    const av = sortValue(a, key);
    const bv = sortValue(b, key);
    const bothNumeric = av !== '' && bv !== '' && !Number.isNaN(Number(av)) && !Number.isNaN(Number(bv));
    return bothNumeric
      ? sign * (Number(av) - Number(bv))
      : sign * av.localeCompare(bv, undefined, { numeric: true, sensitivity: 'base' });
  };
}

const STATUS_COLOR = ['brand', 'green', 'gray'] as const;

export default function FuelSurchargePage() {
  usePageTitle('Fuel surcharge');
  const { data, isPending, isError, error } = useQuery({ queryKey: ['fuel-surcharge'], queryFn: getFuelSurcharge, retry: false });
  const [sort, setSort] = useState<{ key: SortKey; direction: Direction } | null>(null);
  const [page, setPage] = useState(1);

  const rows = useMemo(() => {
    const history = data?.history ?? [];
    return sort ? [...history].sort(compare(sort.key, sort.direction)) : history;
  }, [data, sort]);

  const toggleSort = (key: SortKey) => {
    setSort((s) => ({ key, direction: s?.key === key && s.direction === 'ascending' ? 'descending' : 'ascending' }));
    setPage(1);
  };
  const totalPages = Math.max(1, Math.ceil(rows.length / PAGE_SIZE));
  const start = (page - 1) * PAGE_SIZE;
  const end = Math.min(start + PAGE_SIZE, rows.length);

  if (error instanceof ApiError && error.status === 403) return <Navigate to="/" replace />;

  return (
    <div id="urgent-hub">
      <PageHeader eyebrow="Fuel Surcharge" today={data?.today} />
      {isPending && (
        <Center py="xl">
          <Loader aria-label="Loading fuel surcharge" />
        </Center>
      )}
      {isError && <Alert color="red">Couldn't load fuel surcharge records. Try again.</Alert>}
      {data && !data.hasData && (
        <Card>
          <Group justify="center" gap="sm">
            <Icon icon={IconGasStation} set="tabler" size={28} />
            <Text>No fuel surcharge records are available for your account.</Text>
          </Group>
        </Card>
      )}
      {data?.hasData && (
        <Card>
          <SimpleGrid cols={{ base: 1, sm: 3 }} mb="lg">
            <Stat label="Current standard" value={formatPercent(data.currentStandard?.rate)}
              caption={data.currentStandard ? `From ${formatShortDate(data.currentStandard.start)}` : 'No active standard surcharge'} />
            <Stat label="Pump price" value={formatPump(data.pumpPrice)} caption="Latest current record" />
            <Stat label="Visible records" value={String(data.history.length)} />
          </SimpleGrid>

          {rows.length > 0 && (
            <div>
              <Title order={3}>Recent records</Title>
              <Text c="dimmed" mb="sm">
                Recent fuel surcharge records.
              </Text>
              <Table.ScrollContainer minWidth={640}>
                <Table highlightOnHover className="fuel-history-table">
                  <Table.Thead>
                    <Table.Tr>
                      {COLUMNS.map(({ key, label, end: alignEnd }) => {
                        const active = sort?.key === key ? sort.direction : 'none';
                        return (
                          <Table.Th
                            key={key}
                            scope="col"
                            aria-sort={active}
                            ta={alignEnd ? 'right' : undefined}
                            style={{ whiteSpace: 'nowrap' }}
                          >
                            <UnstyledButton fz="inherit" fw="inherit" onClick={() => toggleSort(key)}>
                              {label} <Icon icon={active === 'ascending' ? ChevronUp : active === 'descending' ? ChevronDown : ChevronsUpDown} size={12} />
                            </UnstyledButton>
                          </Table.Th>
                        );
                      })}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {rows.slice(start, end).map((r) => (
                      <Table.Tr key={r.fuelSurchargeId}>
                        <Table.Td>
                          <Badge variant="light" color={STATUS_COLOR[statusOf(r)]}>{statusLabel(r)}</Badge>
                        </Table.Td>
                        <Table.Td>
                          <Badge variant="outline" color="gray">{r.scopeLabel}</Badge>
                        </Table.Td>
                        <Table.Td ta="right" fw={600}>{formatPercent(r.rate)}</Table.Td>
                        <Table.Td ta="right">{formatPump(r.pumpPrice)}</Table.Td>
                        <Table.Td>{formatShortDate(r.start)}</Table.Td>
                        <Table.Td>{formatShortDate(r.end)}</Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
              {totalPages > 1 && (
                <Group justify="space-between" mt="md" component="nav" aria-label="Fuel surcharge pagination">
                  <Text size="sm" c="dimmed">
                    Showing {start + 1}–{end} of {rows.length}
                  </Text>
                  <Text size="sm">
                    Page {page} of {totalPages}
                  </Text>
                  <Pagination total={totalPages} value={page} onChange={setPage} size="sm" withEdges={false} />
                </Group>
              )}
            </div>
          )}
        </Card>
      )}
    </div>
  );
}

function Stat({ label, value, caption }: { label: string; value: string; caption?: string }) {
  return (
    <Card withBorder={false} bg="var(--dd-surface-container-high)" ta="center">
      <Text size="sm" c="dimmed">{label}</Text>
      <Text fz={28} fw={700}>{value}</Text>
      {caption && <Text size="xs" c="dimmed">{caption}</Text>}
    </Card>
  );
}
