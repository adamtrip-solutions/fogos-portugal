import { describe, expect, it } from 'vitest'

import {
  hotspotTimeRange,
  hotspotsAtTime,
  mergeHotspots,
} from './hotspots.ts'
import type { Hotspots, HotspotSample } from './types.ts'

function sample(iso: string | null, lng = -8, lat = 40): HotspotSample {
  return {
    position: { latitude: lat, longitude: lng },
    acquiredAt: iso,
    brightness: 320,
    confidence: 'nominal',
  }
}

function hotspots(
  viirs: HotspotSample[],
  modis: HotspotSample[] = [],
): Hotspots {
  return { incidentId: 'x', viirs, modis, fetchedAt: '2026-07-04T00:00:00Z' }
}

describe('mergeHotspots', () => {
  it('merges VIIRS + MODIS and sorts ascending by time', () => {
    const merged = mergeHotspots(
      hotspots(
        [sample('2026-07-04T12:00:00Z')],
        [sample('2026-07-04T10:00:00Z')],
      ),
    )
    expect(merged.map((p) => p.source)).toEqual(['modis', 'viirs'])
    expect(merged[0].ts).toBeLessThan(merged[1].ts)
  })

  it('drops samples without a parseable AcquiredAt', () => {
    const merged = mergeHotspots(
      hotspots([sample('2026-07-04T12:00:00Z'), sample(null), sample('nope')]),
    )
    expect(merged).toHaveLength(1)
  })

  it('returns an empty list for null hotspots', () => {
    expect(mergeHotspots(null)).toEqual([])
    expect(mergeHotspots(undefined)).toEqual([])
  })
})

describe('hotspotTimeRange', () => {
  it('spans first to last acquisition', () => {
    const points = mergeHotspots(
      hotspots([
        sample('2026-07-04T08:00:00Z'),
        sample('2026-07-04T14:00:00Z'),
        sample('2026-07-04T11:00:00Z'),
      ]),
    )
    const range = hotspotTimeRange(points)
    expect(range).toEqual({
      start: Date.parse('2026-07-04T08:00:00Z'),
      end: Date.parse('2026-07-04T14:00:00Z'),
    })
  })

  it('is null with no points', () => {
    expect(hotspotTimeRange([])).toBeNull()
  })
})

describe('hotspotsAtTime', () => {
  const points = mergeHotspots(
    hotspots([
      sample('2026-07-04T08:00:00Z'),
      sample('2026-07-04T12:00:00Z'),
      sample('2026-07-04T16:00:00Z'),
    ]),
  )
  const range = hotspotTimeRange(points)!

  it('includes only points at or before the scrub time', () => {
    const visible = hotspotsAtTime(
      points,
      Date.parse('2026-07-04T12:00:00Z'),
      range,
    )
    expect(visible).toHaveLength(2)
  })

  it('tags recency relative to the full span (older = lower)', () => {
    const visible = hotspotsAtTime(points, range.end, range)
    const oldest = visible.find((p) => p.ts === range.start)!
    const newest = visible.find((p) => p.ts === range.end)!
    expect(oldest.recency).toBeCloseTo(0)
    expect(newest.recency).toBeCloseTo(1)
  })

  it('gives every visible point full recency when the span is zero', () => {
    const single = mergeHotspots(hotspots([sample('2026-07-04T09:00:00Z')]))
    const r = hotspotTimeRange(single)!
    const visible = hotspotsAtTime(single, r.end, r)
    expect(visible[0].recency).toBe(1)
  })

  it('excludes everything before the range start', () => {
    const visible = hotspotsAtTime(points, range.start - 1000, range)
    expect(visible).toHaveLength(0)
  })
})
