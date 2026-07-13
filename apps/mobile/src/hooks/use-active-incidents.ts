import { queryOptions } from '@tanstack/react-query'

import { fetchActiveIncidents } from '@/lib/fogos/api'

/** Cache key for the live active-incidents feed (persisted across launches). */
export const ACTIVE_INCIDENTS_KEY = ['incidents', 'active'] as const

/**
 * Live `activeIncidents` feed (codes 3–6). Polls every 60 s in the foreground
 * only — TanStack suspends the interval when `focusManager` reports the app
 * backgrounded and `onlineManager` reports offline. Persisted so a cold offline
 * launch shows last-known fires immediately.
 */
export function activeIncidentsQueryOptions() {
  return queryOptions({
    queryKey: ACTIVE_INCIDENTS_KEY,
    queryFn: ({ signal }) => fetchActiveIncidents(signal),
    refetchInterval: 60_000, // foreground only (refetchIntervalInBackground defaults false)
    staleTime: 55_000,
  })
}
