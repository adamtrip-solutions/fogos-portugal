import { describe, expect, it } from 'vitest'

import { nearestWithinRadius } from './map-hit-test.ts'
import type { HitCandidate } from './map-hit-test.ts'

describe('nearestWithinRadius', () => {
  it('returns null when there are no candidates', () => {
    expect(nearestWithinRadius({ x: 0, y: 0 }, [], 24)).toBeNull()
  })

  it('returns null when the nearest candidate is outside the radius', () => {
    const candidates: HitCandidate[] = [{ id: 'a', x: 100, y: 100 }]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 24)).toBeNull()
  })

  it('returns a candidate within the radius', () => {
    const candidates: HitCandidate[] = [{ id: 'a', x: 10, y: 10 }]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 24)).toBe('a')
  })

  it('picks the closest of several candidates in range', () => {
    const candidates: HitCandidate[] = [
      { id: 'far', x: 20, y: 0 },
      { id: 'near', x: 5, y: 0 },
      { id: 'mid', x: 12, y: 0 },
    ]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 24)).toBe('near')
  })

  it('treats the radius as inclusive (distance exactly on the edge)', () => {
    const candidates: HitCandidate[] = [{ id: 'edge', x: 24, y: 0 }]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 24)).toBe('edge')
  })

  it('measures euclidean distance across both axes', () => {
    // (3,4) is exactly 5 px away — inside a radius of 5, outside a radius of 4.
    const candidates: HitCandidate[] = [{ id: 'p', x: 3, y: 4 }]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 5)).toBe('p')
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 4)).toBeNull()
  })

  it('keeps the earlier candidate when two are equidistant', () => {
    const candidates: HitCandidate[] = [
      { id: 'first', x: 10, y: 0 },
      { id: 'second', x: -10, y: 0 },
    ]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 24)).toBe('first')
  })

  it('never matches with a non-positive radius', () => {
    const candidates: HitCandidate[] = [{ id: 'a', x: 0, y: 0 }]
    expect(nearestWithinRadius({ x: 0, y: 0 }, candidates, 0)).toBeNull()
  })
})
