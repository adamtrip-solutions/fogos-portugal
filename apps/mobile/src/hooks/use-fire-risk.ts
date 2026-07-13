import { queryOptions } from '@tanstack/react-query'

import type { RiskDay } from '@fogos/api-client'

import { fetchFireRiskCountry } from '@/lib/fogos/api'

/** Cache key for the national fire-risk choropleth, one entry per horizon. */
export const fireRiskKey = (day: RiskDay) => ['risk', 'country', day] as const

/**
 * Query options for the Risco national choropleth. Forecasts refresh a few times
 * a day, so a generous 30 min stale window + foreground-only refetch is plenty
 * (matching web's cadence). In-memory only — the key never matches the persisted
 * map-feed keys in lib/query.ts.
 */
export function fireRiskQueryOptions(day: RiskDay) {
  return queryOptions({
    queryKey: fireRiskKey(day),
    queryFn: ({ signal }) => fetchFireRiskCountry(day, signal),
    staleTime: 30 * 60_000,
    refetchInterval: 30 * 60_000,
  })
}
