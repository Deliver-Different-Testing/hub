import type { CSSProperties } from 'react';
import { Alert, Center, Loader } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { getHome } from '../api/hub';
import type { HubTile } from '../api/types';
import TenantLogo from '../components/TenantLogo';
import { useMe } from '../hooks/useMe';
import { usePageTitle } from '../hooks/usePageTitle';
import { formatHeaderDate } from '../lib/format';
import { brandForCountry, SEEDS } from '../styles/md3';
import classes from './home.module.css';

/** Entry stagger per tile; capped so a long catalogue never makes the last tile wait seconds. */
export const TILE_STAGGER_MS = 40;
export const TILE_STAGGER_MAX_INDEX = 30;

const delayFor = (index: number) => `${Math.min(index, TILE_STAGGER_MAX_INDEX) * TILE_STAGGER_MS}ms`;

// The keyline and hover bar run from DFRNT Cyan for every tenant, as they did on the Razor page.
const surfaceStyle = { '--hub-accent': SEEDS.dfrnt } as CSSProperties;

function Tile({ tile, index }: { tile: HubTile; index: number }) {
  const style = { animationDelay: delayFor(index) };
  const body = (
    <>
      <div className={classes.iconWell} data-part="icon-well">
        <img className={classes.icon} src={tile.icon} alt="" />
      </div>
      <div className={classes.title}>{tile.title}</div>
    </>
  );

  if (tile.isDisabled) {
    return (
      <div className={`${classes.card} ${classes.soon}`} aria-disabled="true" title="Coming soon" style={style} data-tile={tile.key}>
        <span className={classes.chip} data-part="chip">
          Coming soon
        </span>
        {body}
      </div>
    );
  }
  return (
    <a className={classes.card} href={tile.href} target="_blank" rel="noopener noreferrer" style={style} data-tile={tile.key}>
      {body}
    </a>
  );
}

export default function HomePage() {
  usePageTitle('App Hub');
  const { data: me } = useMe();
  const { data, isPending, isError } = useQuery({ queryKey: ['home'], queryFn: getHome, retry: false });
  const brand = brandForCountry(me?.currentTenant?.countryCode);

  return (
    <div id="urgent-hub" className={classes.surface} data-brand={brand} data-testid="hub-surface" style={surfaceStyle}>
      <div className={classes.header} data-testid="hub-header">
        <div className={classes.brand}>
          <span className={classes.logo}>
            <TenantLogo height={84} />
          </span>
          <span className={classes.eyebrow}>App Hub</span>
        </div>
        {data?.today && <p className={classes.date}>{formatHeaderDate(data.today)}</p>}
      </div>
      {isPending && (
        <Center py="xl">
          <Loader aria-label="Loading tiles" />
        </Center>
      )}
      {isError && (
        <Alert color="red" mt="lg">
          Couldn't load your apps. Try again.
        </Alert>
      )}
      {data && (
        <div className={classes.grid}>
          {data.tiles.map((tile, i) => (
            <Tile key={tile.key} tile={tile} index={i} />
          ))}
        </div>
      )}
    </div>
  );
}
