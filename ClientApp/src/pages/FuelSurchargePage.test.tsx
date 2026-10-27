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
    expect(screen.queryByText('Current standard')).not.toBeInTheDocument();
  });

  it('renders only the surviving stat cards', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('Current standard')).toBeInTheDocument();
    expect(screen.getByText('Pump price', { selector: 'p, div' })).toBeInTheDocument();
    expect(screen.getByText('Visible records')).toBeInTheDocument();
    expect(screen.queryByText(/Client-specific/)).not.toBeInTheDocument();
    expect(screen.queryByText('No client override active')).not.toBeInTheDocument();
  });

  it('scales the rate to a percent with two decimals and formats pump price and the start date', async () => {
    serve(fuelData({ currentStandard: fuelRow(1, { rate: 0.1234, start: '2026-01-05T00:00:00' }), pumpPrice: 2.5 }));
    renderWithProviders(<FuelSurchargePage />);
    expect((await screen.findAllByText('12.34%')).length).toBeGreaterThan(0);
    expect(screen.getByText('From 5 Jan 2026')).toBeInTheDocument();
    expect(screen.getAllByText('2.5000').length).toBeGreaterThan(0);
    expect(screen.getByText('Latest current record')).toBeInTheDocument();
  });

  it('explains when there is no active standard surcharge', async () => {
    serve(fuelData({ currentStandard: null, pumpPrice: null }));
    renderWithProviders(<FuelSurchargePage />);
    expect(await screen.findByText('No active standard surcharge')).toBeInTheDocument();
  });

  it('shows the surviving history columns and no Client column', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');
    expect(screen.getAllByRole('columnheader').map((h) => h.textContent?.trim())).toEqual([
      'Status', 'Scope', 'Rate', 'Pump price', 'Start', 'End',
    ]);
    expect(screen.queryByRole('columnheader', { name: 'Client' })).not.toBeInTheDocument();
  });

  it('marks every column sortable with aria-sort="none" initially', async () => {
    serve();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');
    const headers = screen.getAllByRole('columnheader');
    expect(headers).toHaveLength(6);
    headers.forEach((h) => {
      expect(h).toHaveAttribute('aria-sort', 'none');
      expect(within(h).getByRole('button')).toBeInTheDocument();
    });
  });

  it('renders status, scope chips and the Open end date', async () => {
    serve(fuelData({ history: [fuelRow(1, { isCurrent: true }), fuelRow(2, { active: false, end: '2026-03-01T00:00:00' })] }));
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');
    const [first, second] = bodyRows();
    expect(cell(first, 0)).toHaveTextContent('Current');
    expect(cell(first, 5)).toHaveTextContent('Open');
    expect(cell(second, 0)).toHaveTextContent('Inactive');
    expect(cell(second, 1)).toHaveTextContent('Standard');
    expect(cell(second, 5)).toHaveTextContent('1 Mar 2026');
  });

  it('pages the history ten rows at a time with a range label', async () => {
    serve(fuelData({ history: manyRows(25) }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');
    expect(bodyRows()).toHaveLength(10);
    expect(screen.getByText('Showing 1–10 of 25')).toBeInTheDocument();
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
    expect(screen.getByText('25', { selector: 'p' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: '3' }));
    expect(bodyRows()).toHaveLength(5);
    expect(screen.getByText('Showing 21–25 of 25')).toBeInTheDocument();
    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument();
  });

  it('hides the pager when everything fits on one page', async () => {
    serve(fuelData({ history: manyRows(10) }));
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');
    expect(screen.queryByRole('navigation', { name: 'Fuel surcharge pagination' })).not.toBeInTheDocument();
  });

  it('sorts a numeric column across all pages and flips direction, resetting to page 1', async () => {
    serve(fuelData({ history: manyRows(25) }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');
    await user.click(screen.getByRole('button', { name: '2' }));

    const rate = await sortBy(user, /Rate/);
    expect(rate).toHaveAttribute('aria-sort', 'ascending');
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
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
    await screen.findByText('Recent records');

    const scope = screen.getByRole('columnheader', { name: /Scope/ });
    within(scope).getByRole('button').focus();
    await user.keyboard('{Enter}');
    expect(scope).toHaveAttribute('aria-sort', 'ascending');
    await user.keyboard(' ');
    expect(scope).toHaveAttribute('aria-sort', 'descending');
    expect(cell(bodyRows()[0], 1)).toHaveTextContent('Scope 12');
  });

  it('sorts by status, pump price and dates (open end dates sort last)', async () => {
    serve(fuelData({ history: [
      fuelRow(1, { end: null, start: '2026-02-01T00:00:00', pumpPrice: 3, active: false }),
      fuelRow(2, { end: '2026-05-01T00:00:00', start: '2026-01-01T00:00:00', pumpPrice: null, isCurrent: true }),
    ] }));
    const user = userEvent.setup();
    renderWithProviders(<FuelSurchargePage />);
    await screen.findByText('Recent records');

    await sortBy(user, /Status/);
    expect(cell(bodyRows()[0], 0)).toHaveTextContent('Current');
    await sortBy(user, /End/);
    expect(cell(bodyRows()[1], 5)).toHaveTextContent('Open');
    await sortBy(user, /Start/);
    expect(cell(bodyRows()[0], 4)).toHaveTextContent('1 Jan 2026');
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
