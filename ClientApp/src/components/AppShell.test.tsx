import { fireEvent, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { antiforgeryHandler, logoHandler, ME_NZ } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import AppShell from './AppShell';

const shell = (me = ME_NZ) => (
  <Routes>
    <Route element={<AppShell me={me} />}>
      <Route path="/FuelSurcharge" element={<div>page</div>} />
    </Route>
  </Routes>
);

async function askAndCapture(me = ME_NZ) {
  let body: { pageContext?: string } | undefined;
  server.use(
    logoHandler(),
    antiforgeryHandler(),
    http.get('/api/assistant/status', () => HttpResponse.json({ enabled: true })),
    http.post('/api/assistant/messages', async ({ request }) => {
      body = (await request.json()) as typeof body;
      return new HttpResponse(`data: ${JSON.stringify({ type: 'done' })}\n\n`);
    }),
  );
  renderWithProviders(shell(me), { route: '/FuelSurcharge' });
  fireEvent.click(await screen.findByRole('button', { name: 'Open Auto-mate assistant' }));
  fireEvent.click(await screen.findByRole('button', { name: 'What can I open from the App Hub?' }));
  await screen.findByRole('textbox', { name: 'Your question' });
  await vi.waitFor(() => expect(body).toBeDefined());
  return body;
}

describe('AppShell assistant', () => {
  it('mounts the assistant with the route and organisation as page context', async () => {
    expect((await askAndCapture())?.pageContext).toBe('Page: /FuelSurcharge; Organisation: Urgent');
  });

  it('omits the organisation when there is no current one', async () => {
    expect((await askAndCapture({ ...ME_NZ, currentTenant: null }))?.pageContext).toBe('Page: /FuelSurcharge');
  });
});
