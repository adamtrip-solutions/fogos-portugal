/**
 * Screen-space hit testing for the fire markers.
 *
 * The map badges are a MapLibre *symbol* layer, whose hit testing
 * (`queryRenderedFeatures`) depends on the placement/collision index. That index
 * is recomputed lazily after camera moves, so during/just after an animation
 * (fly-to on select, radar frame repaints) a query at the click point can miss a
 * badge that is plainly visible — the click then deselects until a manual zoom
 * forces re-placement.
 *
 * Instead we hit-test in screen space: project every displayed incident's
 * coordinate to CSS pixels ourselves and pick the nearest within a
 * touch-friendly radius. This never touches symbol placement, so it is immune to
 * the stale-index bug.
 */

export interface ScreenPoint {
  x: number
  y: number
}

/** A candidate marker projected to CSS pixels. */
export interface HitCandidate {
  id: string
  x: number
  y: number
}

/**
 * Returns the id of the candidate closest to `point` within `radius` CSS
 * pixels, or `null` when none qualify. Distance ties keep the earlier
 * candidate. `radius` is treated as inclusive; non-positive radii never match.
 */
export function nearestWithinRadius(
  point: ScreenPoint,
  candidates: HitCandidate[],
  radius: number,
): string | null {
  if (radius <= 0) return null

  const maxDistSq = radius * radius
  let bestId: string | null = null
  let bestDistSq = Infinity

  for (const candidate of candidates) {
    const dx = candidate.x - point.x
    const dy = candidate.y - point.y
    const distSq = dx * dx + dy * dy
    if (distSq <= maxDistSq && distSq < bestDistSq) {
      bestDistSq = distSq
      bestId = candidate.id
    }
  }

  return bestId
}
