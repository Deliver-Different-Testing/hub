import { Anchor, Box, Container, Group, Text, useMantineTheme } from '@mantine/core';
import type { CSSProperties } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import type { Me } from '../api/types';
import ColorModeToggle from './ColorModeToggle';
import OrgSwitcher from './OrgSwitcher';
import ProfileMenu from './ProfileMenu';
import TenantLogo from './TenantLogo';
import { brandForCountry, SEEDS } from '../styles/md3';
import home from '../styles/homeBackground.module.css';

// The App Hub keeps the Razor page's patterned backdrop; every other page sits on the plain surface.
const homeAccent = { '--home-accent': SEEDS.dfrnt } as CSSProperties;

/** Authenticated frame: Ink-Blue header (logo, org switcher, profile, colour mode), page, footer. */
export default function AppShell({ me }: { me: Me }) {
  const { shell } = useMantineTheme().other;
  const isHome = useLocation().pathname === '/';

  return (
    <Box
      mih="100vh"
      bg={isHome ? undefined : 'var(--dd-surface)'}
      className={isHome ? home.homeField : undefined}
      data-brand={brandForCountry(me.currentTenant?.countryCode)}
      data-testid="app-shell"
      style={{ display: 'flex', flexDirection: 'column', ...(isHome ? homeAccent : {}) }}
    >
      <Box component="header" bg={shell.appBar} mih={64} py="xs">
        <Container size={1500} w="85%" miw={0} maw="100%" h="100%">
          <Group justify="space-between" wrap="nowrap" gap="sm">
            <Anchor component={Link} to="/" aria-label="Home">
              <TenantLogo onShell />
            </Anchor>
            <Group gap="md" wrap="nowrap">
              <OrgSwitcher me={me} />
              <ProfileMenu me={me} />
              <ColorModeToggle />
            </Group>
          </Group>
        </Container>
      </Box>
      <Container component="main" size={1500} w="85%" maw="100%" py="md" style={{ flex: 1 }}>
        <Outlet />
      </Container>
      <Box component="footer" py="md">
        <Container size={1500} w="85%" maw="100%">
          <Text size="sm" c="dimmed">
            &copy; {new Date().getFullYear()} Deliver DFRNT
          </Text>
        </Container>
      </Box>
    </Box>
  );
}
