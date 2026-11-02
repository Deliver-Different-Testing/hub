import { useEffect, useMemo, type ReactNode } from 'react';
import { Center, Loader, MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import UpdatePrompt from './components/UpdatePrompt';
import { isAnonymousPath } from './constants';
import { useColorMode } from './hooks/useColorMode';
import { useMe } from './hooks/useMe';
import { usePrefersMoreContrast } from './hooks/usePrefersMoreContrast';
import { useTenantAppearance } from './hooks/useTenantAppearance';
import { toThemeAppearance } from './styles/appearance';
import { createDfrntTheme, dfrntCssVariablesResolver } from './styles/mantineTheme';

/**
 * Themes the app from the current tenant's appearance (primary, secondary, text colours, contrast) on top
 * of the built-in DFRNT cyan / Ink theme. On authenticated routes nothing renders until api/me and the
 * appearance have resolved, so a branded tenant never flashes cyan; anonymous routes (api/me is 401 there)
 * render straight away in the built-in theme. A failed or empty appearance lookup also means built-in.
 */
export default function ThemedApp({ children }: { children: ReactNode }) {
  const { mode } = useColorMode();
  const { data: me, isPending } = useMe();
  const { data: settings, isLoading: appearanceLoading } = useTenantAppearance(me?.currentTenant?.id);
  const systemPrefersMore = usePrefersMoreContrast();
  const appearance = useMemo(() => toThemeAppearance(settings ?? null, systemPrefersMore), [settings, systemPrefersMore]);
  const theme = useMemo(() => createDfrntTheme(appearance), [appearance]);
  const holdBack = (isPending || appearanceLoading) && !isAnonymousPath(window.location.pathname);

  // Hook for the rules in styles/highContrast.css that CSS variables can't express.
  useEffect(() => {
    document.documentElement.dataset.ddContrast = appearance.highContrast ? 'high' : 'standard';
  }, [appearance.highContrast]);

  return (
    <MantineProvider
      theme={theme}
      forceColorScheme={mode}
      env={import.meta.env.MODE === 'test' ? 'test' : 'default'}
      cssVariablesResolver={dfrntCssVariablesResolver}
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
