import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '../test/utils';
import ColorModeToggle from './ColorModeToggle';

describe('ColorModeToggle', () => {
  it('offers Light, Dark and System and marks the current preference', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Theme' }));

    expect(await screen.findByRole('menuitem', { name: 'Light' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Dark' })).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'System' })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('menuitem', { name: 'Light' })).toHaveAttribute('aria-current', 'false');
  });

  it('shows the theme in use on the button, like dfrntcrm, with no tooltip', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ColorModeToggle />);
    const button = screen.getByRole('button', { name: 'Theme' });
    expect(button.querySelector('.lucide-sun')).not.toBeNull();

    await user.hover(button);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();

    await user.click(button);
    await user.click(await screen.findByRole('menuitem', { name: 'Dark' }));
    expect(button.querySelector('.lucide-moon')).not.toBeNull();
    expect(button.querySelector('.lucide-sun-moon')).toBeNull();
  });

  it('gives System the icon of the theme in use rather than a system glyph', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Theme' }));
    const system = await screen.findByRole('menuitem', { name: 'System' });
    expect(system.querySelector('.lucide-sun')).not.toBeNull();
    expect(document.querySelector('.lucide-sun-moon')).toBeNull();
  });

  it('persists the choice under hub-color-mode and restores it on the next mount', async () => {
    const user = userEvent.setup();
    const first = renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Theme' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Dark' }));

    expect(window.localStorage.getItem('hub-color-mode')).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    first.unmount();

    renderWithProviders(<ColorModeToggle />);
    await user.click(screen.getByRole('button', { name: 'Theme' }));
    expect(await screen.findByRole('menuitem', { name: 'Dark' })).toHaveAttribute('aria-current', 'true');
  });
});
