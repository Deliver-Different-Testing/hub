import { http, HttpResponse } from 'msw';
import { screen } from '@testing-library/react';
import { Route, Routes } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import AgentAccessUnavailablePage from './AgentAccessUnavailablePage';

describe('AgentAccessUnavailablePage', () => {
  it('says why AI app access is unavailable', async () => {
    server.use(
      http.get('/api/agent-access/unavailable', () =>
        HttpResponse.json({ message: 'AI app access is switched off for Urgent Couriers. Ask your administrator to turn it on.' }),
      ),
    );
    renderWithProviders(<AgentAccessUnavailablePage />, { route: '/agent-access/unavailable' });

    expect(screen.queryByRole('heading', { name: 'AI app access' })).not.toBeInTheDocument();
    expect(await screen.findByText(/switched off for Urgent Couriers/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'AI app access' })).toBeInTheDocument();
    expect(screen.getByText('You can close this window.')).toBeInTheDocument();
  });

  it('does not exist while AI app access is dark, as if Hub had no OAuth server', async () => {
    server.use(http.get('/api/agent-access/unavailable', () => HttpResponse.json({ error: 'Not found.' }, { status: 404 })));
    renderWithProviders(
      <Routes>
        <Route path="/agent-access/unavailable" element={<AgentAccessUnavailablePage />} />
        <Route path="/" element={<p>home page</p>} />
      </Routes>,
      { route: '/agent-access/unavailable' },
    );

    expect(await screen.findByText('home page')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'AI app access' })).not.toBeInTheDocument();
  });

  it('says it could not load the reason when the server fails', async () => {
    server.use(http.get('/api/agent-access/unavailable', () => HttpResponse.json({ error: 'Boom' }, { status: 500 })));
    renderWithProviders(<AgentAccessUnavailablePage />, { route: '/agent-access/unavailable' });

    expect(await screen.findByText(/Couldn't check AI app access/)).toBeInTheDocument();
  });
});
