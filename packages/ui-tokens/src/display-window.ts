// Live-map display-window predicate — the SINGLE source of truth for which
// incidents render as map dots. Pure, no React/RN imports. Web will adopt this
// (its current twin lives in apps/web/src/lib/fogos/format.ts as
// WINDOW_HOURS + isHiddenFromMap; this package unifies both into one predicate).

import { isOngoingStatus, statusBucket } from './status'

/**
 * Finished fires (the `done` bucket) stay on the map for this long after the
 * status last changed; ongoing fires (isOngoingStatus) are never time-windowed
 * by this value.
 */
export const DISPLAY_WINDOW_HOURS = 3

/**
 * Em Resolução (7) / Vigilância (9) are "ongoing", so they never drop off via
 * DISPLAY_WINDOW_HOURS — they can sit on the map indefinitely once crews leave
 * or the record goes quiet. These two thresholds hide such "zombie" fires.
 */
export const DEMOBILIZED_HIDE_HOURS = 12
export const STALE_HIDE_HOURS = 24

const HOUR_MS = 60 * 60 * 1000

/** Minimal structural shape the predicate reads — satisfied by IncidentListItem. */
export interface DisplayWindowIncident {
  status: { code: number }
  /** When the status last changed; null when no transition was ever recorded. */
  statusChangedAt: string | null
  occurredAt: string
  resources: { man: number }
  signals: {
    /**
     * When the fire was first reported fully demobilized, or null/undefined.
     * NOT deployed by the backend yet — treated as null until it ships, so the
     * demobilized rule stays dark. `-1`/unknown never counts as demobilized.
     */
    demobilizedSince?: string | null
  }
}

/**
 * Map-DOT-only hide rule for Em Resolução (7) / Vigilância (9) fires. Returns
 * true when such a fire should drop off the LIVE MAP; it stays in every other
 * surface (list, deep links, detail). Never hides any other bucket.
 *
 *  1. Demobilized: `signals.demobilizedSince` is ≥ DEMOBILIZED_HIDE_HOURS old —
 *     crews stood down that long ago but the fire was never closed. (Field is
 *     absent from the query documents until the backend deploys it → treated as
 *     null → this branch stays dark for now.)
 *  2. Stale AND unmanned: the status last changed ≥ STALE_HIDE_HOURS ago AND the
 *     fire currently has no crew (`resources.man <= 0`; both 0 and the `-1`
 *     unknown sentinel count as unmanned). Keyed on `statusChangedAt` (with an
 *     `occurredAt` fallback), NOT `updatedAt`: the backend's ICNF enrichment job
 *     bumps `updatedAt` unconditionally every few hours on fires of any status,
 *     so `updatedAt` never reads as stale for the recent fires this targets. A
 *     crewed fire is NEVER hidden by staleness, no matter how old its change is.
 */
function isHiddenFromMap(inc: DisplayWindowIncident, now: number): boolean {
  const bucket = statusBucket(inc.status.code)
  if (bucket !== 'resolving' && bucket !== 'vigilancia') return false

  const demobilizedSince = inc.signals.demobilizedSince
  if (
    demobilizedSince != null &&
    now - Date.parse(demobilizedSince) >= DEMOBILIZED_HIDE_HOURS * HOUR_MS
  ) {
    return true
  }

  const lastChange = inc.statusChangedAt ?? inc.occurredAt
  if (
    inc.resources.man <= 0 &&
    now - Date.parse(lastChange) >= STALE_HIDE_HOURS * HOUR_MS
  ) {
    return true
  }

  return false
}

/**
 * Whether an incident should render as a dot on the live map at instant `now`.
 *
 *  - Ongoing fires (3–6, 7, 9) show regardless of age — EXCEPT winding-down 7/9
 *    fires caught by the zombie rules (see {@link isHiddenFromMap}).
 *  - Finished fires (the `done` bucket) show only while the status last changed
 *    within {@link DISPLAY_WINDOW_HOURS} (`statusChangedAt`, `occurredAt`
 *    fallback — NEVER `updatedAt`, which the ICNF enrichment job bulk-bumps).
 */
export function isDisplayedOnMap(
  inc: DisplayWindowIncident,
  now: number = Date.now(),
): boolean {
  if (isHiddenFromMap(inc, now)) return false
  if (isOngoingStatus(inc.status.code)) return true

  const finishedAt = inc.statusChangedAt ?? inc.occurredAt
  return Date.parse(finishedAt) >= now - DISPLAY_WINDOW_HOURS * HOUR_MS
}
