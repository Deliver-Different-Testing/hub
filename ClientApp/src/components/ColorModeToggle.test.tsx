import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '../test/utils';
import ColorModeToggle from './ColorModeToggle';

describe('ColorModeToggle', () => {
  it('offers Light, Dark and System and marks the current preference', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Change theme' }));

    expect(await screen.findByRole('menuitem', { name: 'Light' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Dark' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'System' })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('menuitem', { name: 'Light' })).toHaveAttribute('aria-current', 'false');
  });

  it('persists the choice under hub-color-mode and restores it on the next mount', async () => {
    const user = userEvent.setup();
    const first = renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Change theme' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Dark' }));

    expect(window.localStorage.getItem('hub-color-mode')).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    first.unmount();

    renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Change theme' }));
    expect(await screen.findByRole('menuitem', { name: 'Dark' })).toHaveAttribute('aria-current', 'true');
  });
});
