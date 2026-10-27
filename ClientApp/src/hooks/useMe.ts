import { useQuery } from '@tanstack/react-query';
import { getMe } from '../api/hub';

export const ME_KEY = ['me'] as const;

/**
 * The signed-in user. 401 is an expected outcome on anonymous routes, so it never retries, and a
 * failed result is kept rather than refetched each time another component mounts with this hook.
 * Signing in ends in a full page load, which starts with a fresh cache anyway.
 */
export function useMe() {
  return useQuery({ queryKey: ME_KEY, queryFn: getMe, staleTime: Infinity, retry: false, retryOnMount: false });
}
