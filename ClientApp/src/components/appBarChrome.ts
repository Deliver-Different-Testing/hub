import classes from './appBar.module.css';

/** App bar height, in literal px so it stays out of Mantine's rem/scale calc (same as IntegrationManager). */
export const APP_BAR_HEIGHT_PX = 56;

/** The old Hub navbar's bare icon button: a 36px circle that washes on hover. Colour comes from the bar's --app-bar-on. */
export const appBarIconProps = {
  variant: 'subtle',
  size: 36,
  radius: 'xl',
  className: `${classes.iconButton} ${classes.focusRing}`,
} as const;
