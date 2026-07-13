// Pure map-feed pipeline: merge the active + recent feeds, apply the shared
// display-window predicate, then the user's ephemeral filters — in that order,
// exactly like the web map (apps/web/src/routes/index.tsx). No React/RN imports
// so the steps stay unit-testable and cheap to reason about.

import { isDisplayedOnMap, statusBucket } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'
import type { IncidentListItem } from '@fogos/api-client'

/** Ephemeral (in-memory, not persisted) live-map filters. */
export interface MapFilters {
  /** Selected status buckets; an incident shows only if its bucket is present. */
  buckets: ReadonlySet<StatusBucket>
  /** Hide incidents not updated within this many hours; null = no age limit. */
  maxAgeHours: number | null
}

const HOUR_MS = 60 * 60 * 1000

/**
 * Merge the two feeds by id. The `activeIncidents` version WINS the dedup — web
 * seeds the map with the recent feed then overwrites with active
 * (apps/web/src/routes/index.tsx: `for (const inc of activeList) byId.set(...)`
 * runs last), so a fire present in both keeps its fresher active-feed row.
 */
export function mergeIncidentFeeds(
  active: readonly IncidentListItem[],
  recent: readonly IncidentListItem[],
): IncidentListItem[] {
  const byId = new Map<string, IncidentListItem>()
  for (const inc of recent) byId.set(inc.id, inc)
  for (const inc of active) byId.set(inc.id, inc)
  return [...byId.values()]
}

/**
 * The map's base list: the merged feed passed through the shared display-window
 * predicate (ongoing fires always show; finished ones only within 3h; winding-
 * down 7/9 zombies hidden). This is the pre-filter total the filter badge counts.
 */
export function displayedIncidents(
  merged: readonly IncidentListItem[],
  now: number,
): IncidentListItem[] {
  return merged.filter((inc) => isDisplayedOnMap(inc, now))
}

/**
 * Apply the user's ephemeral filters to the base list — bucket membership plus
 * the "updated within N hours" age cap. Mirrors web's `list` memo
 * (apps/web/src/routes/index.tsx: the `buckets.has(...)` + `updatedAt` age gate).
 */
export function applyMapFilters(
  list: readonly IncidentListItem[],
  filters: MapFilters,
  now: number,
): IncidentListItem[] {
  return list.filter((inc) => {
    if (!filters.buckets.has(statusBucket(inc.status.code))) return false
    if (
      filters.maxAgeHours != null &&
      now - Date.parse(inc.updatedAt) > filters.maxAgeHours * HOUR_MS
    ) {
      return false
    }
    return true
  })
}
