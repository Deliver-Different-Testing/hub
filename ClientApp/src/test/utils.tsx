import type { ReactElement } from 'react';
import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ColorModeProvider } from '../hooks/useColorMode';
import { createDfrntTheme, dfrntCssVariablesResolver } from '../styles/mantineTheme';
import type { ThemeAppearance } from '../styles/appearance';

export function renderWithProviders(
  ui: ReactElement,
  { route = '/', appearance }: { route?: string; appearance?: ThemeAppearance } = {},
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ColorModeProvider>
        <MantineProvider
          env="test"
          theme={createDfrntTheme(appearance)}
          cssVariablesResolver={dfrntCssVariablesResolver}
        >
          <Notifications />
          <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
        </MantineProvider>
      </ColorModeProvider>
    </QueryClientProvider>,
  );
}
