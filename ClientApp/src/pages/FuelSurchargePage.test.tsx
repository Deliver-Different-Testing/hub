import { http, HttpResponse } from 'msw';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import type { FuelSurchargeData } from '../api/types';
import { fuelData, fuelRow, logoHandler, meHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import FuelSurchargePage from './FuelSurchargePage';

const serve = (data: FuelSurchargeData = fuelData()) =>
  server.use(meHandler(), logoHandler(), http.get('/api/fuel-surcharge', () => HttpResponse.json(data)));

const manyRows = (n: number) =>
  Array.from({ length: n }, (_, i) =>
    fuelRow(i + 1, { rate: (i + 1) / 100, scopeLabel: `Scope ${String(i + 1).padStart(2, '0')}`, isCurrent: i === 0, active: i < 5 }),
  );

const bodyRows = () => screen.getAllByRole('row').slice(1);
const sortBy = async (user: ReturnType<typeof userEvent.setup>, name: RegExp) => {
  const header = screen.getByRole('columnheader', { name });
  await user.click(within(header).getByRole('button'));
  return header;
};
const cell = (row: HTMLElement, index: number) => within(row).getAllByRole('cell')[index];

// FuelSurchargeViewTests ported to behaviour.
describe('FuelSurchargePage', () => {
  it('shows the empty state when there are no records', async () => {
    serve(fuelData({ hasData: false, currentStandard: null, history: [] }));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('No fuel surcharge records are available for your account.')).toBeInTheDocument();
    expect(screen.getByText('Rates will show here once Urgent sets a fuel surcharge for your account.')).toBeInTheDocument();
    expect(screen.queryByText('Current standard rate')).not.toBeInTheDocument();
  });

  it('leads with the current standard rate and drops the record-count stat', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('Current standard rate')).toBeInTheDocument();
    expect(screen.getByText('12.34%', { selector: 'p' })).toBeInTheDocument();
    expect(screen.queryByText('Visible records')).not.toBeInTheDocument();
    expect(screen.queryByText(/Client-specific/)).not.toBeInTheDocument();
    expect(screen.queryByText('No client override active')).not.toBeInTheDocument();
  });

  it('scales the rate to a percent with two decimals and formats pump price and the start date', async () => {
    serve(fuelData({ currentStandard: fuelRow(1, { rate: 0.1234, start: '2026-01-05T00:00:00' }), pumpPrice: 2.5 }));
    renderWithProviders(<FuelSurchargePage />);
    expect((await screen.findAllByText('12.34%')).length).toBeGreaterThan(0);
    expect(screen.getByText('In effect from 5 Jan 2026. Pump price 2.5000')).toBeInTheDocument();
  });

  it('explains when there is no active standard surcharge', async () => {
    serve(fuelData({ currentStandard: null, pumpPrice: null }));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('No active standard surcharge')).toBeInTheDocument();
  });

  it('shows the change against the previous standard rate with a step chart of the history', async () => {
    const current = fuelRow(3, { rate: 0.1234, start: '2026-09-01T00:00:00', isCurrent: true });
    serve(fuelData({ currentStandard: current, history: [
      current,
      fuelRow(2, { rate: 0.1184, start: '2026-06-01T00:00:00' }),
      fuelRow(1, { rate: 0.1, start: '2026-01-01T00:00:00' }),
      fuelRow(8, { rate: 0.3, start: '2026-04-01T00:00:00', clientId: 7, scopeLabel: 'Client-specific' }),
      fuelRow(9, { rate: 0.5, start: '2026-05-01T00:00:00', active: false }),
    ] }));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('+0.50 pts since 1 Jun 2026')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Standard rate from 10.00% to 12.34% over 3 rates' })).toBeInTheDocument();
  });

  it('shows a falling rate as a negative change', async () => {
    const current = fuelRow(2, { rate: 0.1, start: '2026-09-01T00:00:00', isCurrent: true });
    serve(fuelData({ currentStandard: current, history: [current, fuelRow(1, { rate: 0.125, start: '2026-06-01T00:00:00' })] }));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('−2.50 pts since 1 Jun 2026')).toBeInTheDocument();
  });

  it('hides the change and the chart when there is only one standard rate', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Current standard rate');
    expect(screen.queryByText(/pts since/)).not.toBeInTheDocument();
    expect(screen.queryByRole('img', { name: /Standard rate from/ })).not.toBeInTheDocument();
  });

  it('announces the next scheduled standard rate', async () => {
    const current = fuelRow(1, { isCurrent: true });
    serve(fuelData({ currentStandard: current, history: [
      fuelRow(5, { rate: 0.14, start: '2026-12-01T00:00:00' }),
      fuelRow(4, { rate: 0.128, start: '2026-11-02T00:00:00' }),
      fuelRow(6, { rate: 0.2, start: '2026-11-01T00:00:00', active: false }),
      current,
    ] }));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('Next change: 12.80% from 2 Nov 2026')).toBeInTheDocument();
  });

  it('has no next change when nothing is scheduled', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Current standard rate');
    expect(screen.queryByText(/Next change/)).not.toBeInTheDocument();
  });

  it('shows the history columns with a merged Effective range and no Client column', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    expect(screen.queryByText('Recent fuel surcharge records.')).not.toBeInTheDocument();
    expect(screen.getAllByRole('columnheader').map((h) => h.textContent?.trim())).toEqual([
      'Status', 'Scope', 'Rate', 'Pump price', 'Effective',
    ]);
    expect(screen.queryByRole('columnheader', { name: 'Client' })).not.toBeInTheDocument();
  });

  it('marks every column sortable with aria-sort="none" initially', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    const headers = screen.getAllByRole('columnheader');
    expect(headers).toHaveLength(5);
    headers.forEach((h) => {
      expect(h).toHaveAttribute('aria-sort', 'none');
      expect(within(h).getByRole('button')).toBeInTheDocument();
    });
  });

  it('shows a sort arrow only on the sorted column', async () => {
    serve();
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    screen.getAllByRole('columnheader').forEach((h) => expect(h.querySelector('svg')).toBeNull());
    const rate = await sortBy(user, /Rate/);
    expect(rate.querySelector('svg')).not.toBeNull();
  });

  it('renders Current, Scheduled, Past and Withdrawn statuses, scope chips and effective ranges', async () => {
    serve(fuelData({ history: [
      fuelRow(1, { isCurrent: true }),
      fuelRow(2, { start: '2026-11-02T00:00:00' }),
      fuelRow(3, { start: '2025-06-01T00:00:00', end: '2026-01-04T00:00:00' }),
      fuelRow(4, { active: false, end: '2026-03-01T00:00:00' }),
    ] }));
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    const [current, scheduled, past, withdrawn] = bodyRows();
    expect(cell(current, 0)).toHaveTextContent('Current');
    expect(current).toHaveAttribute('data-current', 'true');
    expect(cell(current, 4)).toHaveTextContent('5 Jan 2026 – open');
    expect(cell(scheduled, 0)).toHaveTextContent('Scheduled');
    expect(scheduled).not.toHaveAttribute('data-current');
    expect(cell(past, 0)).toHaveTextContent('Past');
    expect(cell(past, 4)).toHaveTextContent('1 Jun 2025 – 4 Jan 2026');
    expect(cell(withdrawn, 0)).toHaveTextContent('Withdrawn');
    expect(cell(withdrawn, 1)).toHaveTextContent('Standard');
    expect(cell(withdrawn, 4)).toHaveTextContent('5 Jan 2026 – 1 Mar 2026');
  });

  it('pages the history ten rows at a time with a range label', async () => {
    serve(fuelData({ history: manyRows(25) }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    expect(bodyRows()).toHaveLength(10);
    expect(screen.getByText('Showing 1–10 of 25')).toBeInTheDocument();
    expect(screen.queryByText(/Page \d of/)).not.toBeInTheDocument();
    expect(screen.getByText('25 records')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '3' }));
    expect(bodyRows()).toHaveLength(5);
    expect(screen.getByText('Showing 21–25 of 25')).toBeInTheDocument();
  });

  it('hides the pager when everything fits on one page', async () => {
    serve(fuelData({ history: manyRows(10) }));
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    expect(screen.queryByRole('navigation', { name: 'Fuel surcharge pagination' })).not.toBeInTheDocument();
  });

  it('sorts a numeric column across all pages and flips direction, resetting to page 1', async () => {
    serve(fuelData({ history: manyRows(25) }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');
    await user.click(screen.getByRole('button', { name: '2' }));

    const rate = await sortBy(user, /Rate/);
    expect(rate).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByText('Showing 1–10 of 25')).toBeInTheDocument();
    expect(cell(bodyRows()[0], 2)).toHaveTextContent('1.00%');

    await sortBy(user, /Rate/);
    expect(rate).toHaveAttribute('aria-sort', 'descending');
    expect(cell(bodyRows()[0], 2)).toHaveTextContent('25.00%');
    screen.getAllByRole('columnheader').filter((h) => h !== rate).forEach((h) => expect(h).toHaveAttribute('aria-sort', 'none'));
  });

  it('sorts text columns and is keyboard operable', async () => {
    serve(fuelData({ history: manyRows(12) }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');

    const scope = screen.getByRole('columnheader', { name: /Scope/ });
    within(scope).getByRole('button').focus();
    await user.keyboard('{Enter}');
    expect(scope).toHaveAttribute('aria-sort', 'ascending');
    await user.keyboard(' ');
    expect(scope).toHaveAttribute('aria-sort', 'descending');
    expect(cell(bodyRows()[0], 1)).toHaveTextContent('Scope 12');
  });

  it('sorts by status in Current, Scheduled, Past, Withdrawn order, then by effective start and pump price', async () => {
    serve(fuelData({ history: [
      fuelRow(1, { start: '2026-02-01T00:00:00', pumpPrice: 3, active: false }),
      fuelRow(2, { start: '2025-01-01T00:00:00', pumpPrice: 2 }),
      fuelRow(3, { start: '2026-12-01T00:00:00', pumpPrice: 1 }),
      fuelRow(4, { start: '2026-01-01T00:00:00', pumpPrice: null, isCurrent: true }),
    ] }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Rate history');

    await sortBy(user, /Status/);
    expect(bodyRows().map((r) => cell(r, 0).textContent)).toEqual(['Current', 'Scheduled', 'Past', 'Withdrawn']);
    await sortBy(user, /Effective/);
    expect(cell(bodyRows()[0], 4)).toHaveTextContent('1 Jan 2025');
    await sortBy(user, /Pump price/);
    expect(cell(bodyRows()[0], 3)).toHaveTextContent('—');
  });

  it('shows an error when the records cannot be loaded', async () => {
    server.use(meHandler(), logoHandler(), http.get('/api/fuel-surcharge', () => new HttpResponse(null, { status: 500 })));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText("Couldn't load fuel surcharge records. Try again.")).toBeInTheDocument();
  });

  it('sends tenants without fuel surcharge access back home, like the old page did', async () => {
    server.use(
      meHandler(),
      logoHandler(),
      http.get('/api/fuel-surcharge', () => HttpResponse.json({ error: 'Forbidden' }, { status: 403 })),
    );
    renderWithProviders(
      <Routes>
        <Route path="/" element={<p>home page</p>} />
        <Route path="/FuelSurcharge" element={<FuelSurchargePage />} />
      </Routes>,
      { route: '/FuelSurcharge' },
    );
    expect(await screen.findByText('home page')).toBeInTheDocument();
  });
});
