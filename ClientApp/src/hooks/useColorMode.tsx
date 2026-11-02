/**
 * DFRNT light/dark color-mode provider + hook. Portable copy from the
 * IntegrationManager AdminPortal. Defaults to the OS theme ('system'), persists
 * an explicit 'light'/'dark' pick to localStorage, and follows OS changes live.
 *
 * Wire it ABOVE MantineProvider and feed the resolved `mode` into
 * `<MantineProvider forceColorScheme={mode}>` so there is a single source of truth:
 *
 *   <ColorModeProvider>
 *     <ThemedApp />          // reads useColorMode().mode → forceColorScheme
 *   </ColorModeProvider>
 *
 * `Md3Mode` is imported from ./md3. Split the context out to a separate file if
 * your bundler enforces react-refresh "only export components" (see note below).
 */
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import type { Md3Mode } from '../styles/md3';

const STORAGE_KEY = 'hub-color-mode';
const DARK_QUERY = '(prefers-color-scheme: dark)';

export type ColorModePreference = Md3Mode | 'system';

export interface ColorModeContextValue {
  mode: Md3Mode;
  preference: ColorModePreference;
  setPreference: (preference: ColorModePreference) => void;
}

const DEFAULT_VALUE: ColorModeContextValue = {
  mode: 'light',
  preference: 'system',
  setPreference: () => {},
};

// NOTE: exporting both the context/hook AND the provider component from one file
// trips Vite's react-refresh "only export components" rule. In the source app these
// are split (colorModeContext.ts + useColorMode.tsx). Kept together here for a
// single copy-paste asset; split them if your lint config complains.
export const ColorModeContext = createContext<ColorModeContextValue>(DEFAULT_VALUE);

export function useColorMode(): ColorModeContextValue {
  return useContext(ColorModeContext);
}

function readStoredPreference(): ColorModePreference {
  if (typeof window === 'undefined') return 'system';
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
  } catch {
    /* localStorage unavailable — fall through to default */
  }
  return 'system';
}

function getSystemMode(): Md3Mode {
  try {
    if (window.matchMedia?.(DARK_QUERY).matches) return 'dark';
  } catch {
    /* matchMedia unavailable — fall through to default */
  }
  return 'light';
}

function resolveMode(preference: ColorModePreference): Md3Mode {
  return preference === 'system' ? getSystemMode() : preference;
}

function persist(preference: ColorModePreference): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, preference);
  } catch {
    /* ignore */
  }
}

export function ColorModeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ColorModePreference>(readStoredPreference);
  const [mode, setModeState] = useState<Md3Mode>(() => resolveMode(readStoredPreference()));
  const preferenceRef = useRef<ColorModePreference>(preference);

  const setPreference = useCallback((next: ColorModePreference) => {
    preferenceRef.current = next;
    setPreferenceState(next);
    setModeState(resolveMode(next));
    persist(next);
  }, []);

  // Follow OS theme changes live while the preference is 'system'.
  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;
    const mql = window.matchMedia(DARK_QUERY);
    const onChange = (e: MediaQueryListEvent) => {
      if (preferenceRef.current === 'system') setModeState(e.matches ? 'dark' : 'light');
    };
    mql.addEventListener?.('change', onChange);
    return () => mql.removeEventListener?.('change', onChange);
  }, []);

  // Keep native UI (scrollbars, form controls, autofill) in sync with the mode.
  useEffect(() => {
    document.documentElement.style.colorScheme = mode;
  }, [mode]);

  const value = useMemo(
    () => ({ mode, preference, setPreference }),
    [mode, preference, setPreference],
  );
  return <ColorModeContext.Provider value={value}>{children}</ColorModeContext.Provider>;
}
