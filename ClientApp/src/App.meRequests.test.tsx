import { http, HttpResponse } from 'msw';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import App from './App';
import { server } from './test/server';

// The real App (ThemedApp + router + shared QueryClient), not the AppRoutes test harness, because
// the duplicate requests come from several useMe() observers mounting at different times.
describe('api/me requests for a signed-out visitor', () => {
  it('asks the server once, then lands on the login page', async () => {
    let calls = 0;
    server.use(
      http.get('/api/me', () => {
        calls += 1;
        return HttpResponse.json({ error: 'Unauthorized' }, { status: 401 });
      }),
    );
    window.history.pushState({}, '', '/');

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 100));
    expect(window.location.pathname).toBe('/Account/Login');
    expect(calls).toBe(1);
  });
});
