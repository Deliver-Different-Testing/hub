import { useQuery } from '@tanstack/react-query';
import { getTenantAppearance } from '../api/hub';
import { parseAppearanceSettings } from '../styles/appearance';

/**
 * The signed-in tenant's appearance defaults, or null when none are set or the lookup fails (the
 * built-in theme then applies). Disabled until a tenant id is known, so anonymous routes never call it.
 */
export function useTenantAppearance(tenantId: number | undefined) {
  return useQuery({
    queryKey: ['tenant-appearance', tenantId],
    queryFn: async () => parseAppearanceSettings(await getTenantAppearance(tenantId!)),
    enabled: tenantId !== undefined,
    staleTime: Infinity,
    retry: false,
  });
}
