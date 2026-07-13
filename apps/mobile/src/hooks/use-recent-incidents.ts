import { queryOptions } from '@tanstack/react-query'

import { fetchRecentIncidents } from '@/lib/fogos/api'

/** Cache key for the recent + finished feed (persisted across launches). */
export const RECENT_INCIDENTS_KEY = ['incidents', 'recent'] as const

/**
 * Recent + finished feed (the `updatedAfter` breadth pages plus the 7/9
 * truncation-guard tail). Same 60 s foreground poll as the active feed; merged
 * with it by the map pipeline. Persisted for offline cold starts.
 */
export function recentIncidentsQueryOptions() {
  return queryOptions({
    queryKey: RECENT_INCIDENTS_KEY,
    queryFn: ({ signal }) => fetchRecentIncidents(signal),
    refetchInterval: 60_000,
    staleTime: 55_000,
  })
}
