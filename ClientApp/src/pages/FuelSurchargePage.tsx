import { useMemo, useState } from 'react';
import { Alert, Badge, Card, Center, Group, Loader, Pagination, Stack, Table, Text, Title, UnstyledButton } from '@mantine/core';
import { IconGasStation } from '@tabler/icons-react';
import { useQuery } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { ChevronDown, ChevronUp, TrendingDown, TrendingUp } from 'lucide-react';
import { ApiError } from '../api/client';
import { getFuelSurcharge } from '../api/hub';
import type { FuelSurchargeData, FuelSurchargeRow } from '../api/types';
import Icon from '../components/Icon';
import PageHeader from '../components/PageHeader';
import { usePageTitle } from '../hooks/usePageTitle';
import { formatPercent, formatPump, formatShortDate } from '../lib/format';
import classes from './fuelSurcharge.module.css';
import RateStepChart, { type RatePoint } from './RateStepChart';

export const PAGE_SIZE = 10;
const CHART_POINTS = 12;

type SortKey = 'status' | 'scope' | 'rate' | 'pump' | 'effective';
type Direction = 'ascending' | 'descending';

const COLUMNS: { key: SortKey; label: string; end?: boolean }[] = [
  { key: 'status', label: 'Status' },
  { key: 'scope', label: 'Scope' },
  { key: 'rate', label: 'Rate', end: true },
  { key: 'pump', label: 'Pump price', end: true },
  { key: 'effective', label: 'Effective' },
];

const STATUS = [
  { label: 'Current', color: 'brand' },
  { label: 'Scheduled', color: 'blue' },
  { label: 'Past', color: 'gray' },
  { label: 'Withdrawn', color: 'red' },
] as const;

const isoDate = (d: string | null) => (d ? d.slice(0, 10) : '9999-12-31');
const decimal = (n: number | null) => (n == null ? '' : n.toFixed(4));

/** Current, then rates still to start, then ones that have been replaced, then withdrawn. */
function statusOf(r: FuelSurchargeRow, today: string): number {
  if (r.isCurrent) return 0;
  if (!r.active) return 3;
  return isoDate(r.start) > isoDate(today) ? 1 : 2;
}

const effective = (r: FuelSurchargeRow) => `${formatShortDate(r.start)} – ${r.end ? formatShortDate(r.end) : 'open'}`;

function sortValue(r: FuelSurchargeRow, key: SortKey, today: string): string {
  switch (key) {
    case 'status': return String(statusOf(r, today));
    case 'scope': return r.scopeLabel;
    case 'rate': return decimal(r.rate);
    case 'pump': return decimal(r.pumpPrice);
    case 'effective': return isoDate(r.start);
  }
}

function compare(key: SortKey, direction: Direction, today: string) {
  const sign = direction === 'descending' ? -1 : 1;
  return (a: FuelSurchargeRow, b: FuelSurchargeRow) => {
    const av = sortValue(a, key, today);
    const bv = sortValue(b, key, today);
    const bothNumeric = av !== '' && bv !== '' && !Number.isNaN(Number(av)) && !Number.isNaN(Number(bv));
    return bothNumeric
      ? sign * (Number(av) - Number(bv))
      : sign * av.localeCompare(bv, undefined, { numeric: true, sensitivity: 'base' });
  };
}

const isStandard = (r: FuelSurchargeRow): r is FuelSurchargeRow & { rate: number; start: string } =>
  r.clientId == null && r.active && r.rate != null && r.start != null;

/** Active standard rates that have started, oldest first. */
function standardTimeline(data: FuelSurchargeData): RatePoint[] {
  const today = isoDate(data.today);
  return data.history
    .filter(isStandard)
    .filter((r) => isoDate(r.start) <= today)
    .map((r) => ({ start: r.start, rate: r.rate }))
    .sort((a, b) => a.start.localeCompare(b.start));
}

function nextScheduled(data: FuelSurchargeData) {
  const today = isoDate(data.today);
  return data.history
    .filter(isStandard)
    .filter((r) => isoDate(r.start) > today)
    .sort((a, b) => a.start.localeCompare(b.start))[0];
}

export default function FuelSurchargePage() {
  usePageTitle('Fuel surcharge');
  const { data, isPending, isError, error } = useQuery({ queryKey: ['fuel-surcharge'], queryFn: getFuelSurcharge, retry: false });
  const [sort, setSort] = useState<{ key: SortKey; direction: Direction } | null>(null);
  const [page, setPage] = useState(1);
  const today = data?.today ?? '';

  const rows = useMemo(() => {
    const history = data?.history ?? [];
    return sort ? [...history].sort(compare(sort.key, sort.direction, data?.today ?? '')) : history;
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
          <Stack align="center" gap={4} py="md">
            <Icon icon={IconGasStation} set="tabler" size={28} />
            <Text fw={600}>No fuel surcharge records are available for your account.</Text>
            <Text c="dimmed" size="sm">Rates will show here once Urgent sets a fuel surcharge for your account.</Text>
          </Stack>
        </Card>
      )}
      {data?.hasData && (
        <Stack gap="xl">
          <RateReadout data={data} />

          {rows.length > 0 && (
            <Card component="section" withBorder padding="lg" aria-labelledby="fuel-history-title">
              <Group gap="sm" align="baseline" mb="sm">
                <Title order={3} id="fuel-history-title">Rate history</Title>
                <Text c="dimmed" size="sm">{rows.length} {rows.length === 1 ? 'record' : 'records'}</Text>
              </Group>
              <Table.ScrollContainer minWidth={520}>
                <Table highlightOnHover className={`fuel-history-table ${classes.table}`}>
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
                              {label}
                              {active !== 'none' && <> <Icon icon={active === 'ascending' ? ChevronUp : ChevronDown} size={12} /></>}
                            </UnstyledButton>
                          </Table.Th>
                        );
                      })}
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {rows.slice(start, end).map((r) => {
                      const status = STATUS[statusOf(r, today)];
                      return (
                        <Table.Tr key={r.fuelSurchargeId} data-current={r.isCurrent || undefined}>
                          <Table.Td>
                            <Badge variant="light" color={status.color}>{status.label}</Badge>
                          </Table.Td>
                          <Table.Td>
                            <Badge variant="outline" color="gray">{r.scopeLabel}</Badge>
                          </Table.Td>
                          <Table.Td ta="right" fw={600} className={classes.num}>{formatPercent(r.rate)}</Table.Td>
                          <Table.Td ta="right" className={classes.num}>{formatPump(r.pumpPrice)}</Table.Td>
                          <Table.Td style={{ whiteSpace: 'nowrap' }}>{effective(r)}</Table.Td>
                        </Table.Tr>
                      );
                    })}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
              {totalPages > 1 && (
                <Group justify="space-between" mt="md" component="nav" aria-label="Fuel surcharge pagination">
                  <Text size="sm" c="dimmed">
                    Showing {start + 1}–{end} of {rows.length}
                  </Text>
                  <Pagination total={totalPages} value={page} onChange={setPage} size="sm" withEdges={false} />
                </Group>
              )}
            </Card>
          )}
        </Stack>
      )}
    </div>
  );
}

function RateReadout({ data }: { data: FuelSurchargeData }) {
  const current = data.currentStandard;
  const timeline = standardTimeline(data);
  const index = current ? timeline.findIndex((p) => p.start === current.start && p.rate === current.rate) : -1;
  const previous = index > 0 ? timeline[index - 1] : undefined;
  const next = nextScheduled(data);

  return (
    <section className={classes.readout} aria-label="Current fuel surcharge">
      <Text c="dimmed" fw={500}>Current standard rate</Text>
      {current ? (
        <>
          <Group gap="md" align="baseline" wrap="wrap" mt={4}>
            <Text className={classes.rate}>{formatPercent(current.rate)}</Text>
            {previous && current.rate != null && <Delta from={previous} to={current.rate} />}
          </Group>
          <Text c="dimmed" size="sm" mt="xs">
            In effect from {formatShortDate(current.start)}. Pump price {formatPump(data.pumpPrice)}
          </Text>
        </>
      ) : (
        <Text mt={4} fw={600}>No active standard surcharge</Text>
      )}
      <RateStepChart points={timeline.slice(-CHART_POINTS)} today={data.today} />
      {next && (
        <Text size="sm" fw={500} mt="sm">
          Next change: {formatPercent(next.rate)} from {formatShortDate(next.start)}
        </Text>
      )}
    </section>
  );
}

/** Change in percentage points; not coloured good/bad, a surcharge moving is just information. */
function Delta({ from, to }: { from: RatePoint; to: number }) {
  const points = (to - from.rate) * 100;
  const sign = points > 0 ? '+' : points < 0 ? '−' : '±';
  return (
    <Text c="dimmed" className={classes.delta}>
      {points !== 0 && <Icon icon={points > 0 ? TrendingUp : TrendingDown} size={16} />}
      {`${sign}${Math.abs(points).toFixed(2)} pts since ${formatShortDate(from.start)}`}
    </Text>
  );
}
