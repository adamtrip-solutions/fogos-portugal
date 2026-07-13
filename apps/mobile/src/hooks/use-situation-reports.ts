import { queryOptions } from '@tanstack/react-query'

import { fetchSituationReports } from '@/lib/fogos/api'

/** Cache-key prefix for the nationwide situation-report archive. */
export const SITUATION_REPORTS_KEY = ['reports', 'situation'] as const

/**
 * Query options for the Situação screen. Fetches the `first` newest reports in
 * one round-trip. Refetches every 5 min in the FOREGROUND only (TanStack
 * suspends the interval when `focusManager` reports the app backgrounded — see
 * the root layout's AppState wiring); `refetchIntervalInBackground` stays false.
 * Mirrors web's 5 min cadence with a 4 min `staleTime`. In-memory only (the key
 * never matches the persisted map-feed keys in lib/query.ts).
 */
export function situationReportsQueryOptions(first: number) {
  return queryOptions({
    queryKey: [...SITUATION_REPORTS_KEY, first] as const,
    queryFn: ({ signal }) => fetchSituationReports(first, signal),
    refetchInterval: 5 * 60_000,
    staleTime: 4 * 60_000,
  })
}
