import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

// Tenants have no branding unless a test says otherwise.
export const server = setupServer(http.get('/api/tenant/:tenantId/appearance', () => new HttpResponse(null, { status: 204 })));
