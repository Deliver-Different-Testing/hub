import type { ReactNode } from 'react';
import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ColorModeProvider, useColorMode } from './useColorMode';

const wrapper = ({ children }: { children: ReactNode }) => <ColorModeProvider>{children}</ColorModeProvider>;

describe('useColorMode', () => {
  it('defaults to the system preference (light when the OS is light)', () => {
    const { result } = renderHook(() => useColorMode(), { wrapper });
    expect(result.current.preference).toBe('system');
    expect(result.current.mode).toBe('light');
  });

  it('persists an explicit choice under the hub-color-mode key and applies it', () => {
    const { result } = renderHook(() => useColorMode(), { wrapper });
    act(() => result.current.setPreference('dark'));
    expect(result.current.mode).toBe('dark');
    expect(window.localStorage.getItem('hub-color-mode')).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
  });

  it('restores the stored preference on mount', () => {
    window.localStorage.setItem('hub-color-mode', 'dark');
    const { result } = renderHook(() => useColorMode(), { wrapper });
    expect(result.current.preference).toBe('dark');
    expect(result.current.mode).toBe('dark');
  });

  it('ignores an invalid stored value', () => {
    window.localStorage.setItem('hub-color-mode', 'purple');
    const { result } = renderHook(() => useColorMode(), { wrapper });
    expect(result.current.preference).toBe('system');
  });
});
