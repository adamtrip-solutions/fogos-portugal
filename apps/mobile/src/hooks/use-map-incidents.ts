import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'

import type { IncidentListItem } from '@fogos/api-client'
import {
  applyMapFilters,
  displayedIncidents,
  mergeIncidentFeeds,
  type MapFilters,
} from '@/lib/fogos/map-feed'
import { activeIncidentsQueryOptions } from '@/hooks/use-active-incidents'
import { recentIncidentsQueryOptions } from '@/hooks/use-recent-incidents'

export interface MapIncidentsState {
  /** Incidents to render, after the display window AND the user's filters. */
  incidents: IncidentListItem[]
  /** Displayed-on-map count BEFORE the user's filters (the filter-badge total). */
  baseCount: number
  /** True only on the very first load with nothing to show yet. */
  loading: boolean
  isError: boolean
  /** Newest of the two feeds' successful-fetch timestamps (0 until first load). */
  dataUpdatedAt: number
}

/**
 * The full live-map data pipeline: poll both feeds (60 s, foreground only), merge
 * by id (active wins), apply the shared display-window predicate, then the user's
 * ephemeral filters. Mirrors the web map's `baseList` → `list` derivation.
 *
 * `Date.now()` is read at render — fine, since both feeds refetch every 60 s so
 * the age-based cuts never drift more than a poll interval behind.
 */
export function useMapIncidents(filters: MapFilters): MapIncidentsState {
  const active = useQuery(activeIncidentsQueryOptions())
  const recent = useQuery(recentIncidentsQueryOptions())

  // Both feeds refetch every 60 s, so recomputing the age-based cuts against
  // `Date.now()` when the data changes keeps them fresh — the same approach as
  // web's map (apps/web/src/routes/index.tsx). The purity rule flags reading the
  // clock in render; that's intentional and bounded to a poll interval here.
  const base = useMemo(
    () =>
      displayedIncidents(
        mergeIncidentFeeds(active.data ?? [], recent.data ?? []),
        // eslint-disable-next-line react-hooks/purity -- refetch-bounded, see above
        Date.now(),
      ),
    [active.data, recent.data],
  )

  const incidents = useMemo(
    // eslint-disable-next-line react-hooks/purity -- refetch-bounded, see above
    () => applyMapFilters(base, filters, Date.now()),
    [base, filters],
  )

  const loading = (active.isLoading || recent.isLoading) && incidents.length === 0

  return {
    incidents,
    baseCount: base.length,
    loading,
    isError: active.isError || recent.isError,
    dataUpdatedAt: Math.max(active.dataUpdatedAt, recent.dataUpdatedAt),
  }
}
