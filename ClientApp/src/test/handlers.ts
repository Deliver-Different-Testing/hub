import { http, HttpResponse } from 'msw';
import type { FuelSurchargeData, FuelSurchargeRow, HomeData, Me } from '../api/types';

export const ME_NZ: Me = {
  email: 'jane.doe@example.com',
  displayName: 'Jane Doe',
  isCourier: false,
  currentTenant: { id: 1, name: 'Urgent', code: 'urgent', countryCode: 'NZ' },
  tenants: [
    { id: 1, name: 'Urgent', code: 'urgent', countryCode: 'NZ' },
    { id: 2, name: 'Acme Delivery', code: 'acme', countryCode: 'US' },
  ],
};

export const ME_US: Me = {
  ...ME_NZ,
  currentTenant: { id: 2, name: 'Acme Delivery', code: 'acme', countryCode: 'US' },
};

export const meHandler = (me: Me = ME_NZ) => http.get('/api/me', () => HttpResponse.json(me));
export const unauthorizedMeHandler = () =>
  http.get('/api/me', () => HttpResponse.json({ error: 'Unauthorized' }, { status: 401 }));
export const antiforgeryHandler = () => http.get('/api/antiforgery', () => new HttpResponse(null, { status: 204 }));
export const logoHandler = (logoUrl: string | null = '/images/DFRNT_HorizLogo_RGB.png') =>
  http.get('/api/Logo', () => HttpResponse.json({ success: true, logoUrl, isLocal: logoUrl?.startsWith('/') ?? true }));

export const tile = (n: number, over: Partial<HomeData['tiles'][number]> = {}) => ({
  key: `hub-tile-${n}`,
  title: `Tile ${n}`,
  icon: `/images/icons/tile-${n}.svg`,
  href: `https://app${n}.example.com`,
  isDisabled: false,
  ...over,
});

export const homeData = (tiles = [tile(1), tile(2)]): HomeData => ({
  userEmail: 'jane.doe@example.com',
  tenantCode: 'urgent',
  today: '2026-10-27T09:00:00',
  isCourier: false,
  showAfterHours: false,
  tiles,
});

export const fuelRow = (id: number, over: Partial<FuelSurchargeRow> = {}): FuelSurchargeRow => ({
  fuelSurchargeId: id,
  clientId: null,
  clientName: null,
  rate: 0.1234,
  pumpPrice: 2.5,
  start: '2026-01-05T00:00:00',
  end: null,
  active: true,
  isCurrent: false,
  scopeLabel: 'Standard',
  ...over,
});

export const fuelData = (over: Partial<FuelSurchargeData> = {}): FuelSurchargeData => {
  const current = fuelRow(1, { isCurrent: true });
  return { hasData: true, today: '2026-10-27T09:00:00', pumpPrice: 2.5, currentStandard: current, history: [current], ...over };
};
