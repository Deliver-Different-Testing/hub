import type { ReactElement } from 'react';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ColorModeProvider } from '../hooks/useColorMode';
import { createDfrntCssVariablesResolver, createDfrntTheme } from '../styles/mantineTheme';
import type { Brand } from '../styles/md3';

export function renderWithProviders(
  ui: ReactElement,
  { route = '/', brand = 'dfrnt' }: { route?: string; brand?: Brand } = {},
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ColorModeProvider>
        <MantineProvider
          env="test"
          theme={createDfrntTheme(brand)}
          cssVariablesResolver={createDfrntCssVariablesResolver(brand)}
        >
          <Notifications />
          <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
        </MantineProvider>
      </ColorModeProvider>
    </QueryClientProvider>,
  );
}
