import type { ReactNode } from 'react';
import { useMantineTheme } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { ColorModeProvider } from './hooks/useColorMode';
import { ME_NZ, ME_US, meHandler, unauthorizedMeHandler } from './test/handlers';
import { server } from './test/server';
import ThemedApp from './ThemedApp';
import { createDfrntTheme } from './styles/mantineTheme';

const INK_SHELL = createDfrntTheme().other?.shell?.appBar;
const CYAN = createDfrntTheme().colors?.brand?.[5];

function ThemeProbe() {
  const theme = useMantineTheme();
  return (
    <div data-testid="probe">
      {theme.other.shell.appBar} {theme.colors.brand[5]} {theme.other.appearance.highContrast ? 'high' : 'standard'}
    </div>
  );
}

const appearanceHandler = (tenantId: number, body: unknown) =>
  http.get(`/api/tenant/${tenantId}/appearance`, () =>
    body === null ? new HttpResponse(null, { status: 204 }) : HttpResponse.json(body as object),
  );

const renderThemed = (children: ReactNode = <ThemeProbe />) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ColorModeProvider>
        <ThemedApp>{children}</ThemedApp>
      </ColorModeProvider>
    </QueryClientProvider>,
  );

describe('ThemedApp', () => {
  it('holds back rendering on authenticated routes until the tenant and its appearance are known', async () => {
    server.use(meHandler(ME_NZ), appearanceHandler(1, null));
    renderThemed(<div data-testid="child" />);
    expect(screen.queryByTestId('child')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Loading')).toBeInTheDocument();
    expect(await screen.findByTestId('child')).toBeInTheDocument();
  });

  it('themes every country in built-in cyan on Ink when the tenant has no appearance (204)', async () => {
    server.use(meHandler(ME_NZ), appearanceHandler(1, null));
    const nz = renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent(`${INK_SHELL} ${CYAN} standard`);
    nz.unmount();

    server.use(meHandler(ME_US), appearanceHandler(2, null));
    renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent(`${INK_SHELL} ${CYAN} standard`);
  });

  it("applies the tenant's primary to the brand ramp and the shell", async () => {
    server.use(meHandler(ME_NZ), appearanceHandler(1, { primary: '#13b964' }));
    renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent('#13b964 #13b964 standard');
  });

  it('ignores an unusable appearance payload', async () => {
    server.use(meHandler(ME_NZ), appearanceHandler(1, { primary: 'green', contrast: 'loud' }));
    renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent(`${INK_SHELL} ${CYAN} standard`);
  });

  it('falls back to the built-in theme when the appearance lookup fails', async () => {
    server.use(meHandler(ME_NZ), http.get('/api/tenant/1/appearance', () => new HttpResponse(null, { status: 500 })));
    renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent(`${INK_SHELL} ${CYAN} standard`);
  });

  it('marks high contrast on <html> when the tenant forces it', async () => {
    server.use(meHandler(ME_NZ), appearanceHandler(1, { contrast: 'high' }));
    renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent('high');
    expect(document.documentElement.dataset.ddContrast).toBe('high');
  });

  it('resolves contrast auto against the OS prefers-contrast setting', async () => {
    const original = window.matchMedia;
    window.matchMedia = ((query: string) => ({
      ...original(query),
      matches: query === '(prefers-contrast: more)',
    })) as typeof window.matchMedia;
    try {
      server.use(meHandler(ME_NZ), appearanceHandler(1, { contrast: 'auto' }));
      renderThemed();
      expect(await screen.findByTestId('probe')).toHaveTextContent('high');
    } finally {
      window.matchMedia = original;
    }
  });

  it('renders anonymous routes immediately on the built-in theme without asking for an appearance', async () => {
    window.history.pushState({}, '', '/Account/Login');
    const appearanceCalls = vi.fn();
    server.use(
      unauthorizedMeHandler(),
      http.get('/api/tenant/:id/appearance', () => {
        appearanceCalls();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderThemed();
    expect(screen.getByTestId('probe')).toHaveTextContent(`${INK_SHELL} ${CYAN} standard`);
    await waitFor(() => expect(screen.getByTestId('probe')).toBeInTheDocument());
    expect(appearanceCalls).not.toHaveBeenCalled();
  });

  it('falls back to the built-in theme and renders the children when an authenticated route gets a 401', async () => {
    server.use(unauthorizedMeHandler());
    renderThemed();
    expect(await screen.findByTestId('probe')).toHaveTextContent(`${INK_SHELL} ${CYAN} standard`);
  });
});
