import { useEffect, useState } from 'react';

const QUERY = '(prefers-contrast: more)';

/** Whether the OS asks for more contrast, following live changes. */
export function usePrefersMoreContrast(): boolean {
  const [matches, setMatches] = useState(() => window.matchMedia(QUERY).matches);

  useEffect(() => {
    const query = window.matchMedia(QUERY);
    const onChange = (event: MediaQueryListEvent) => setMatches(event.matches);
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);

  return matches;
}
