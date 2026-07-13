import { queryOptions, useQuery } from '@tanstack/react-query'

import { fetchConcelhoProfile } from '@/lib/fogos/api'

/** Cache key for one concelho's profile. */
export const concelhoProfileKey = (dico: string) => ['concelho', dico] as const

/**
 * Query options for the concelho profile. Refetches every 5 min in the
 * FOREGROUND only (TanStack suspends the interval when backgrounded via
 * `focusManager`), mirroring web's cadence with a 5 min stale window. In-memory
 * only (the key never matches the persisted map-feed keys in lib/query.ts).
 */
export function concelhoProfileQueryOptions(dico: string) {
  return queryOptions({
    queryKey: concelhoProfileKey(dico),
    queryFn: ({ signal }) => fetchConcelhoProfile(dico, signal),
    staleTime: 5 * 60_000,
    refetchInterval: 5 * 60_000,
  })
}

/** The profile for one concelho DICO. */
export function useConcelhoProfile(dico: string) {
  return useQuery(concelhoProfileQueryOptions(dico))
}
