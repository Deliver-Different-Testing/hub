import type { ComponentType } from 'react';

type GlyphProps = { size?: number | string; 'aria-hidden'?: boolean; className?: string };

export interface IconProps {
  /** A lucide-react icon (UI chrome) or a @tabler/icons-react icon (logistics glyphs). */
  icon: ComponentType<GlyphProps>;
  set?: 'lucide' | 'tabler';
  /** Brand default is 36; pass a smaller size for inline use in buttons and menus. */
  size?: number;
  stroke?: number;
  className?: string;
}

/** The one icon wrapper: brand-default 36px at 1.25 stroke, decorative (aria-hidden). */
export default function Icon({ icon: Glyph, set = 'lucide', size = 36, stroke = 1.25, className }: IconProps) {
  const strokeProps = set === 'lucide' ? { strokeWidth: stroke } : { stroke };
  return <Glyph size={size} aria-hidden className={className} {...strokeProps} />;
}
