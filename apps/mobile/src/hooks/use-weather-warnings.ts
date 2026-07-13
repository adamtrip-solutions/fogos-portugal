import { queryOptions } from '@tanstack/react-query'

import { fetchWeatherWarnings } from '@/lib/fogos/api'

/** Cache key for the in-force IPMA weather-warnings feed. */
export const WEATHER_WARNINGS_KEY = ['warnings'] as const

/**
 * Query options for the Avisos screen. Refetches every 5 min in the FOREGROUND
 * only (TanStack suspends the interval when `focusManager` reports the app
 * backgrounded — see the root layout's AppState wiring);
 * `refetchIntervalInBackground` stays false. Mirrors web's 5 min cadence with a
 * 1 min `staleTime`. In-memory only (the key never matches the persisted
 * map-feed keys in lib/query.ts).
 */
export function weatherWarningsQueryOptions() {
  return queryOptions({
    queryKey: WEATHER_WARNINGS_KEY,
    queryFn: ({ signal }) => fetchWeatherWarnings(signal),
    refetchInterval: 5 * 60_000,
    staleTime: 60_000,
  })
}
