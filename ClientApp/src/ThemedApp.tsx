import { useMemo, type ReactNode } from 'react';
import { Center, Loader, MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import UpdatePrompt from './components/UpdatePrompt';
import { isAnonymousPath } from './constants';
import { useColorMode } from './hooks/useColorMode';
import { useMe } from './hooks/useMe';
import { brandForCountry } from './styles/md3';
import { createDfrntCssVariablesResolver, createDfrntTheme } from './styles/mantineTheme';

/**
 * Builds the brand from the current tenant's country (non-US = Urgent amber, US/unknown = DFRNT cyan).
 * On authenticated routes nothing renders until api/me resolves, so a non-US tenant never flashes cyan;
 * anonymous routes (api/me is 401 there) render straight away in cyan.
 */
export default function ThemedApp({ children }: { children: ReactNode }) {
  const { mode } = useColorMode();
  const { data: me, isPending } = useMe();
  const brand = brandForCountry(me?.currentTenant?.countryCode);
  const theme = useMemo(() => createDfrntTheme(brand), [brand]);
  const cssVariablesResolver = useMemo(() => createDfrntCssVariablesResolver(brand), [brand]);
  const holdBack = isPending && !isAnonymousPath(window.location.pathname);

  return (
    <MantineProvider
      theme={theme}
      forceColorScheme={mode}
      env={import.meta.env.MODE === 'test' ? 'test' : 'default'}
      cssVariablesResolver={cssVariablesResolver}
    >
      <Notifications position="top-right" autoClose={5000} />
      <UpdatePrompt />
      {holdBack ? (
        <Center mih="100vh">
          <Loader color="gray" aria-label="Loading" />
        </Center>
      ) : (
        children
      )}
    </MantineProvider>
  );
}
