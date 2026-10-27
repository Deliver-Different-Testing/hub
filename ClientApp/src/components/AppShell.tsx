import { Anchor, Box, Container, Group, useMantineTheme } from '@mantine/core';
import type { CSSProperties } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import type { Me } from '../api/types';
import { APP_BAR_HEIGHT_PX } from './appBarChrome';
import ColorModeToggle from './ColorModeToggle';
import OrgSwitcher from './OrgSwitcher';
import ProfileMenu from './ProfileMenu';
import TenantLogo from './TenantLogo';
import { brandForCountry, SEEDS } from '../styles/md3';
import bar from './appBar.module.css';
import footerClasses from './footer.module.css';
import home from '../styles/homeBackground.module.css';

// The App Hub keeps the Razor page's patterned backdrop; every other page sits on the plain surface.
const homeAccent = { '--home-accent': SEEDS.dfrnt } as CSSProperties;

/**
 * Authenticated frame. The app bar is IntegrationManager's size (full-bleed, 56px) with the old Hub navbar's
 * look: tenant logo, then the organization switcher, the profile menu and the theme toggle on the right.
 */
export default function AppShell({ me }: { me: Me }) {
  const { shell, scrim } = useMantineTheme().other;
  const isHome = useLocation().pathname === '/';
  const barStyle = {
    height: APP_BAR_HEIGHT_PX,
    flexShrink: 0,
    backgroundColor: shell.appBar,
    color: scrim.text,
    '--app-bar-on': scrim.text,
    '--app-bar-chip': scrim.fill,
    '--app-bar-chip-hover': scrim.fillStrong,
    '--app-bar-wash': scrim.hoverFill,
  } as CSSProperties;

  return (
    <Box
      mih="100vh"
      bg={isHome ? undefined : 'var(--dd-surface)'}
      className={isHome ? home.homeField : undefined}
      data-brand={brandForCountry(me.currentTenant?.countryCode)}
      data-testid="app-shell"
      style={{ display: 'flex', flexDirection: 'column', ...(isHome ? homeAccent : {}) }}
    >
      <Group component="header" className={bar.bar} px="md" gap="md" wrap="nowrap" justify="space-between" style={barStyle}>
        <Anchor component={Link} to="/" aria-label="Home" className={`${bar.brand} ${bar.focusRing}`}>
          <TenantLogo onShell height={40} />
        </Anchor>
        <Group gap="xs" wrap="nowrap" miw={0}>
          <OrgSwitcher me={me} />
          <ProfileMenu me={me} />
          <ColorModeToggle />
        </Group>
      </Group>
      <Container component="main" size={1500} w="85%" maw="100%" py="md" style={{ flex: 1 }}>
        <Outlet />
      </Container>
      <footer className={footerClasses.footer}>
        <Container size={1500} w="85%" maw="100%">
          <div className={footerClasses.row}>&copy; {new Date().getFullYear()} Deliver DFRNT</div>
        </Container>
      </footer>
    </Box>
  );
}
