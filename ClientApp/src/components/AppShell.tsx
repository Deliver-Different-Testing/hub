import { Anchor, Box, Breadcrumbs, Container, Group, Text, useMantineTheme } from '@mantine/core';
import { ChevronRight } from 'lucide-react';
import type { CSSProperties } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import type { Me } from '../api/types';
import { APP_BAR_HEIGHT_PX, crumbsFor } from './appBarChrome';
import ColorModeToggle from './ColorModeToggle';
import Icon from './Icon';
import OrgSwitcher from './OrgSwitcher';
import ProfileMenu from './ProfileMenu';
import TenantLogo from './TenantLogo';
import { brandForCountry, SEEDS } from '../styles/md3';
import home from '../styles/homeBackground.module.css';

// The App Hub keeps the Razor page's patterned backdrop; every other page sits on the plain surface.
const homeAccent = { '--home-accent': SEEDS.dfrnt } as CSSProperties;

/**
 * Authenticated frame. The app bar matches IntegrationManager and dfrntcrm: a full-bleed 56px shell bar with the
 * logo, a keyline, where you are, then round icon actions on the right.
 */
export default function AppShell({ me }: { me: Me }) {
  const { shell, scrim } = useMantineTheme().other;
  const { pathname } = useLocation();
  const isHome = pathname === '/';
  const crumbs = crumbsFor(pathname);

  return (
    <Box
      mih="100vh"
      bg={isHome ? undefined : 'var(--dd-surface)'}
      className={isHome ? home.homeField : undefined}
      data-brand={brandForCountry(me.currentTenant?.countryCode)}
      data-testid="app-shell"
      style={{ display: 'flex', flexDirection: 'column', ...(isHome ? homeAccent : {}) }}
    >
      <Group
        component="header"
        px="md"
        gap="md"
        wrap="nowrap"
        style={{ height: APP_BAR_HEIGHT_PX, flexShrink: 0, backgroundColor: shell.appBar, color: scrim.text }}
      >
        <Anchor component={Link} to="/" aria-label="Home" style={{ display: 'flex', alignItems: 'center', flexShrink: 0 }}>
          <TenantLogo onShell height={26} />
        </Anchor>

        {/* Keyline separating the brand mark from the location path */}
        <Box visibleFrom="md" data-testid="app-bar-keyline" style={{ width: 1, height: 22, backgroundColor: shell.border, flexShrink: 0 }} />

        <Breadcrumbs
          visibleFrom="md"
          separator={<Icon icon={ChevronRight} size={14} />}
          styles={{ separator: { color: shell.textMuted, marginInline: 6 } }}
        >
          {crumbs.map((crumb, i) => {
            const isLast = i === crumbs.length - 1;
            return (
              <Text
                key={crumb}
                span
                fw={isLast ? 600 : 400}
                aria-current={isLast ? 'page' : undefined}
                style={{ color: isLast ? shell.textPrimary : shell.textSecondary, fontSize: '0.875rem', lineHeight: 1.2 }}
              >
                {crumb}
              </Text>
            );
          })}
        </Breadcrumbs>

        <Box style={{ flex: 1 }} />

        <Group gap={8} wrap="nowrap" miw={0}>
          <OrgSwitcher me={me} />
          <ColorModeToggle />
          <ProfileMenu me={me} />
        </Group>
      </Group>
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
