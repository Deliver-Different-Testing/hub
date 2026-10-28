import { useState } from 'react';
import { Box, useMantineTheme } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { getLogo } from '../api/hub';
import { useColorMode } from '../hooks/useColorMode';
import { useMe } from '../hooks/useMe';

const DFRNT_LOGO = '/images/DFRNT_HorizLogo_RGB.png';
const URGENT_LOGO = '/images/urgentCouriersNewLogo.png';

// "/images/x.png" -> "/images/x_dark.png": local logos ship a light-wordmark variant for dark surfaces.
const darkVariant = (url: string) => {
  const dot = url.lastIndexOf('.');
  return dot > 0 ? `${url.slice(0, dot)}_dark${url.slice(dot)}` : `${url}_dark`;
};

export interface TenantLogoProps {
  /** On the header bar: the reversed artwork on a dark fill, the standard artwork on a light one, whatever the colour mode. */
  onShell?: boolean;
  height?: number;
  alt?: string;
}

export default function TenantLogo({ onShell = false, height = 40, alt = 'Deliver DFRNT' }: TenantLogoProps) {
  const { mode } = useColorMode();
  const { shell } = useMantineTheme().other;
  const { data: me } = useMe();
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  const tenant = me?.currentTenant;

  const { data, isPending } = useQuery({
    queryKey: ['logo', tenant?.id ?? null],
    queryFn: getLogo,
    staleTime: Infinity,
    retry: false,
    enabled: me !== undefined,
  });

  if (isPending) return <Box h={height} aria-hidden />;

  const logoUrl = data?.success && data.logoUrl ? data.logoUrl : DFRNT_LOGO;
  const isLocal = logoUrl.startsWith('/');
  const wantsDark = onShell ? !shell.inkContent : mode === 'dark';
  const preferred = isLocal && wantsDark ? darkVariant(logoUrl) : logoUrl;

  const isUrgent = tenant?.code.toLowerCase() === 'urgent';
  const fallback = isUrgent ? URGENT_LOGO : wantsDark ? darkVariant(DFRNT_LOGO) : DFRNT_LOGO;
  const src = failedSrc === preferred ? fallback : preferred;

  const image = (
    <img
      src={src}
      alt={alt}
      height={height}
      style={{ display: 'block', height, width: 'auto' }}
      onError={() => setFailedSrc(preferred)}
    />
  );

  // Tenant-uploaded art is arbitrary third-party artwork we cannot recolour, so on the shell it sits on a light chip.
  return onShell && !isLocal ? (
    <Box bg="white" p={4} style={{ borderRadius: 'var(--mantine-radius-sm)' }}>
      {image}
    </Box>
  ) : (
    image
  );
}
