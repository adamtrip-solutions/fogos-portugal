import { queryOptions, useQuery } from '@tanstack/react-query'

import { fetchIncident } from '@/lib/fogos/api'

/** Cache key for one incident's full detail (persisted across launches). */
export const incidentDetailKey = (id: string) =>
  ['incidents', 'detail', id] as const

/**
 * Full `incident(id)` detail for the open bottom sheet. Polls every 60 s in the
 * foreground only (matching the map feeds) so an open sheet keeps its resources,
 * status, weather, etc. live; suspends when backgrounded via `focusManager`.
 * Persisted so a re-opened sheet renders instantly from cache.
 */
export function incidentDetailQueryOptions(id: string) {
  return queryOptions({
    queryKey: incidentDetailKey(id),
    queryFn: ({ signal }) => fetchIncident(id, signal),
    refetchInterval: 60_000, // foreground only (refetchIntervalInBackground defaults false)
    staleTime: 55_000,
  })
}

/**
 * Detail query for the selected incident. Disabled (no fetch) while nothing is
 * selected; the sheet still renders instantly from the in-memory list item and
 * fills detail sections in progressively once this resolves.
 */
export function useIncident(id: string | null) {
  return useQuery({
    ...incidentDetailQueryOptions(id ?? ''),
    enabled: id != null,
  })
}
