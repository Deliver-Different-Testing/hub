import type { ReactNode } from 'react';
import { useMantineTheme } from '@mantine/core';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ColorModeProvider } from './hooks/useColorMode';
import { ME_NZ, ME_US, meHandler, unauthorizedMeHandler } from './test/handlers';
import { server } from './test/server';
import ThemedApp from './ThemedApp';
import { createDfrntTheme } from './styles/mantineTheme';

const INK_SHELL = createDfrntTheme('dfrnt').other?.shell?.appBar;
const AMBER_SHELL = createDfrntTheme('urgent').other?.shell?.appBar;

function ShellProbe() {
  const { shell } = useMantineTheme().other;
  return <div data-testid="shell">{shell.appBar}</div>;
}

const renderThemed = (children: ReactNode = <ShellProbe />) =>
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ColorModeProvider>
        <ThemedApp>{children}</ThemedApp>
      </ColorModeProvider>
    </QueryClientProvider>,
  );

describe('ThemedApp', () => {
  it('holds back rendering on authenticated routes until the tenant is known', async () => {
    server.use(meHandler(ME_NZ));
    renderThemed(<div data-testid="child" />);
    expect(screen.queryByTestId('child')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Loading')).toBeInTheDocument();
    expect(await screen.findByTestId('child')).toBeInTheDocument();
  });

  it('themes a US tenant in DFRNT cyan (Ink shell)', async () => {
    server.use(meHandler(ME_US));
    renderThemed();
    expect(await screen.findByTestId('shell')).toHaveTextContent(INK_SHELL!);
  });

  it('themes a non-US tenant in Urgent amber', async () => {
    server.use(meHandler(ME_NZ));
    renderThemed();
    expect(await screen.findByTestId('shell')).toHaveTextContent(AMBER_SHELL!);
  });

  it('uses cyan for a tenant with no country', async () => {
    server.use(meHandler({ ...ME_NZ, currentTenant: { id: 1, name: 'X', code: 'x', countryCode: null } }));
    renderThemed();
    expect(await screen.findByTestId('shell')).toHaveTextContent(INK_SHELL!);
  });

  it('renders anonymous routes immediately in cyan even though api/me is 401', async () => {
    window.history.pushState({}, '', '/Account/Login');
    server.use(unauthorizedMeHandler());
    renderThemed();
    expect(screen.getByTestId('shell')).toHaveTextContent(INK_SHELL!);
    await waitFor(() => expect(screen.getByTestId('shell')).toHaveTextContent(INK_SHELL!));
  });

  it('falls back to cyan and renders the children when an authenticated route gets a 401', async () => {
    server.use(unauthorizedMeHandler());
    renderThemed();
    expect(await screen.findByTestId('shell')).toHaveTextContent(INK_SHELL!);
  });

});
