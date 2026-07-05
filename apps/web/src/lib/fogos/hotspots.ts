import type { Hotspots, HotspotSample } from './types.ts'

/** A single flattened hotspot with a parsed acquisition timestamp (ms). */
export interface HotspotPoint {
  id: string
  lng: number
  lat: number
  /** AcquiredAt in epoch ms. */
  ts: number
  source: 'viirs' | 'modis'
  brightness: number | null
  confidence: string | null
}

/** Inclusive epoch-ms bounds of a hotspot set. */
export interface HotspotTimeRange {
  start: number
  end: number
}

/** A hotspot visible at the current scrub time, tagged with 0..1 recency. */
export interface VisibleHotspot extends HotspotPoint {
  /** 1 = acquired at/near the scrub instant, → 0 for the oldest in range. */
  recency: number
}

function collect(
  samples: HotspotSample[],
  source: 'viirs' | 'modis',
  out: HotspotPoint[],
): void {
  samples.forEach((sample, i) => {
    if (!sample.acquiredAt) return
    const ts = Date.parse(sample.acquiredAt)
    if (Number.isNaN(ts)) return
    out.push({
      id: `${source}-${i}`,
      lng: sample.position.longitude,
      lat: sample.position.latitude,
      ts,
      source,
      brightness: sample.brightness,
      confidence: sample.confidence,
    })
  })
}

/**
 * Merge VIIRS + MODIS samples into a single time-sorted list, dropping samples
 * without a parseable AcquiredAt (they cannot participate in the scrubber).
 */
export function mergeHotspots(
  hotspots: Hotspots | null | undefined,
): HotspotPoint[] {
  if (!hotspots) return []
  const out: HotspotPoint[] = []
  collect(hotspots.viirs, 'viirs', out)
  collect(hotspots.modis, 'modis', out)
  out.sort((a, b) => a.ts - b.ts)
  return out
}

/** Full [first, last] acquisition span, or null when there are no points. */
export function hotspotTimeRange(
  points: HotspotPoint[],
): HotspotTimeRange | null {
  if (points.length === 0) return null
  let start = points[0].ts
  let end = points[0].ts
  for (const p of points) {
    if (p.ts < start) start = p.ts
    if (p.ts > end) end = p.ts
  }
  return { start, end }
}

/**
 * Points acquired at or before `scrubTime`, each tagged with a recency in
 * [0, 1] relative to the full range span (newer → 1). Used to fade older
 * hotspots on the map as the scrubber advances.
 */
export function hotspotsAtTime(
  points: HotspotPoint[],
  scrubTime: number,
  range: HotspotTimeRange,
): VisibleHotspot[] {
  const span = range.end - range.start
  const visible: VisibleHotspot[] = []
  for (const p of points) {
    if (p.ts > scrubTime) continue
    const recency = span > 0 ? (p.ts - range.start) / span : 1
    visible.push({ ...p, recency: Math.min(1, Math.max(0, recency)) })
  }
  return visible
}
