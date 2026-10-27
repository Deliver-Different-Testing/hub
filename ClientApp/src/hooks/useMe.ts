import { useQuery } from '@tanstack/react-query';
import { getMe } from '../api/hub';

export const ME_KEY = ['me'] as const;

/** The signed-in user. 401 is an expected outcome on anonymous routes, so it never retries. */
export function useMe() {
  return useQuery({ queryKey: ME_KEY, queryFn: getMe, staleTime: Infinity, retry: false });
}
