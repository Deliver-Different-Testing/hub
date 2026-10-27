import classes from './growOnHover.module.css';

/** App bar height, in literal px so it stays out of Mantine's rem/scale calc (same as IntegrationManager and dfrntcrm). */
export const APP_BAR_HEIGHT_PX = 56;

/** Props shared by every app bar icon button: round, grows on hover instead of a background wash. Colour comes from `theme.other.scrim.text`. */
export const appBarIconProps = {
  variant: 'subtle',
  size: 'lg',
  radius: 'xl',
  className: classes.growOnHover,
} as const;

/** Breadcrumb trail per authed route: the App Hub, then the page within it. */
const CRUMBS: Record<string, string[]> = {
  '/': ['App Hub'],
  '/account/settings': ['App Hub', 'User Settings'],
  '/fuelsurcharge': ['App Hub', 'Fuel surcharge'],
};

export const crumbsFor = (pathname: string) => CRUMBS[pathname.toLowerCase().replace(/\/+$/, '') || '/'] ?? ['App Hub'];
