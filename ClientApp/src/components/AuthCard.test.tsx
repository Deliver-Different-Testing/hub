import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '../test/utils';
import AuthCard from './AuthCard';

const css = readFileSync(join(__dirname, '../styles/authBackground.module.css'), 'utf8');

describe('AuthCard', () => {
  it('sits the card on the login field background', () => {
    renderWithProviders(<AuthCard>card body</AuthCard>);
    const field = screen.getByTestId('auth-field');
    expect(field.className).toMatch(/field/);
    expect(field).toContainElement(screen.getByText('card body'));
  });

  it('is the page main landmark', () => {
    renderWithProviders(<AuthCard>card body</AuthCard>);
    expect(screen.getByRole('main')).toBe(screen.getByTestId('auth-field'));
  });

  it('keeps the old login field: Ink Blue ground, tiled chevron pattern and three glows', () => {
    expect(css).toMatch(/background-color:\s*var\(--mantine-color-ink-9\)/);
    expect(css).toMatch(/url\("data:image\/svg\+xml/);
    expect(css).toMatch(/var\(--mantine-color-reflex-5\)/);
    expect(css).toMatch(/var\(--mantine-color-brand-5\)/);
    expect(css).toMatch(/background-repeat:\s*repeat, no-repeat, no-repeat, no-repeat/);
    expect(css).toMatch(/background-attachment:\s*fixed, fixed, fixed, fixed/);
  });

  it('uses the same field in light and dark mode, as before', () => {
    expect(css).not.toMatch(/prefers-color-scheme|data-mantine-color-scheme/);
  });
});
