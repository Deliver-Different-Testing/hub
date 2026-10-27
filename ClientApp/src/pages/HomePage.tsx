import { Alert, Badge, Center, Loader } from '@mantine/core';
import { useQuery } from '@tanstack/react-query';
import { getHome } from '../api/hub';
import type { HubTile } from '../api/types';
import PageHeader from '../components/PageHeader';
import { usePageTitle } from '../hooks/usePageTitle';
import classes from './home.module.css';

/** Entry stagger per tile; capped so a long catalogue never makes the last tile wait seconds. */
export const TILE_STAGGER_MS = 40;
export const TILE_STAGGER_MAX_INDEX = 30;

const delayFor = (index: number) => `${Math.min(index, TILE_STAGGER_MAX_INDEX) * TILE_STAGGER_MS}ms`;

function Tile({ tile, index }: { tile: HubTile; index: number }) {
  const style = { animationDelay: delayFor(index) };
  const body = (
    <>
      <img className={classes.icon} src={tile.icon} alt="" />
      <div className={classes.title}>{tile.title}</div>
    </>
  );

  if (tile.isDisabled) {
    return (
      <div className={`${classes.card} ${classes.soon}`} aria-disabled="true" title="Coming soon" style={style} data-tile={tile.key}>
        {body}
        <Badge variant="light" color="gray">
          Coming soon
        </Badge>
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
  const { data, isPending, isError } = useQuery({ queryKey: ['home'], queryFn: getHome, retry: false });

  return (
    <div id="urgent-hub">
      <PageHeader eyebrow="App Hub" today={data?.today} />
      {isPending && (
        <Center py="xl">
          <Loader aria-label="Loading tiles" />
        </Center>
      )}
      {isError && <Alert color="red">Couldn't load your apps. Try again.</Alert>}
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
