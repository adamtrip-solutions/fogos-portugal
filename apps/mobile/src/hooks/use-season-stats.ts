import { queryOptions } from '@tanstack/react-query'
import { lisbonDateDaysAgo } from '@fogos/api-client'

import { fetchSeasonStats } from '@/lib/fogos/api'

/** Cache-key prefix for the season-statistics dashboard. */
export const SEASON_STATS_KEY = ['stats', 'season'] as const

/**
 * The current season year on the Lisbon calendar. Derived from the shared
 * `lisbonDateDaysAgo` helper (YYYY-MM-DD) so the year flips on the Lisbon-tz new
 * year, not the device-local one.
 */
export function currentSeasonYear(): number {
  return Number(lisbonDateDaysAgo(0).slice(0, 4))
}

/**
 * Query options for the Estatísticas dashboard. One `Season` round-trip keyed on
 * the year. Refetches every 5 min in the FOREGROUND only (TanStack suspends the
 * interval when `focusManager` reports the app backgrounded — see the root
 * layout's AppState wiring); `refetchIntervalInBackground` stays false. Mirrors
 * web's 5 min cadence with a ~4 min `staleTime`. NOT persisted (the key never
 * matches the map-feed keys in lib/query.ts) — in-memory only.
 */
export function seasonStatsQueryOptions(year: number) {
  return queryOptions({
    queryKey: [...SEASON_STATS_KEY, year] as const,
    queryFn: ({ signal }) => fetchSeasonStats(year, signal),
    refetchInterval: 5 * 60_000,
    staleTime: 4 * 60_000,
  })
}
