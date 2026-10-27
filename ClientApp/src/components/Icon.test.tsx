import { IconGasStation } from '@tabler/icons-react';
import { render } from '@testing-library/react';
import { Truck } from 'lucide-react';
import { describe, expect, it } from 'vitest';
import Icon from './Icon';

// Replaces IconSystemTests: one wrapper, brand defaults of 36px at 1.25 stroke, decorative by default.
describe('Icon', () => {
  it('defaults a lucide icon to 36px with a 1.25 stroke', () => {
    const { container } = render(<Icon icon={Truck} />);
    const svg = container.querySelector('svg')!;
    expect(svg).toHaveAttribute('width', '36');
    expect(svg).toHaveAttribute('height', '36');
    expect(svg).toHaveAttribute('stroke-width', '1.25');
    expect(svg).toHaveAttribute('aria-hidden', 'true');
  });

  it('applies the same defaults to a tabler icon', () => {
    const { container } = render(<Icon icon={IconGasStation} set="tabler" />);
    const svg = container.querySelector('svg')!;
    expect(svg).toHaveAttribute('width', '36');
    expect(svg).toHaveAttribute('stroke-width', '1.25');
  });

  it('allows a smaller inline size and a custom stroke', () => {
    const { container } = render(<Icon icon={Truck} size={16} stroke={2} className="x" />);
    const svg = container.querySelector('svg')!;
    expect(svg).toHaveAttribute('width', '16');
    expect(svg).toHaveAttribute('stroke-width', '2');
    expect(svg).toHaveClass('x');
  });

  it('does not load a Material Symbols font or ligature icons anywhere in the app shell', async () => {
    const { readFileSync } = await import('node:fs');
    const { join } = await import('node:path');
    const html = readFileSync(join(__dirname, '../../index.html'), 'utf8');
    expect(html).not.toContain('Material+Symbols');
    expect(html).not.toContain('material-symbols');
  });
});
