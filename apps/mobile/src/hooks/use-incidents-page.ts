import { infiniteQueryOptions } from '@tanstack/react-query'
import type { IncidentsFilter } from '@fogos/api-client'

import { fetchIncidentsPage } from '@/lib/fogos/api'

/** Cache-key prefix for the Ocorrências infinite list. */
export const INCIDENTS_PAGE_KEY = ['incidents', 'page'] as const

/**
 * Infinite-query options for the Ocorrências list (pages of 50). Keyed on the
 * resolved `IncidentsFilter` so each window/bucket/district combination caches
 * independently. Matches web: `staleTime` ~60 s and NO `refetchInterval` — this
 * is a search surface, not the live map. NOT persisted (the key never matches
 * the persisted map-feed keys in lib/query.ts), so it stays in-memory only.
 */
export function incidentsPageQueryOptions(filter: IncidentsFilter) {
  return infiniteQueryOptions({
    queryKey: [...INCIDENTS_PAGE_KEY, filter] as const,
    queryFn: ({ pageParam, signal }) =>
      fetchIncidentsPage(filter, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) =>
      last.pageInfo.hasNextPage ? last.pageInfo.endCursor : undefined,
    staleTime: 60_000,
  })
}
