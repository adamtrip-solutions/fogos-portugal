import { useEffect, useRef, useState } from 'react'
import { queryOptions } from '@tanstack/react-query'

// RainViewer precipitation radar. The public index and the tiles both send
// CORS `*`, so everything here runs client-side with no proxy. The index lists
// `past` frames (~13, 10-min steps, ~2h) and `nowcast` frames (short-range
// forecast) — `nowcast` can be an empty array. Radar coverage tops out around
// zoom 7; the raster source is capped there so MapLibre overzooms instead of
// requesting missing tiles.

const WEATHER_MAPS_URL = 'https://api.rainviewer.com/public/weather-maps.json'
const TTL_MS = 5 * 60 * 1000

/** A single radar frame; `nowcast` marks forecast (vs. observed) frames. */
export interface RadarFrame {
  /** Frame valid time, Unix seconds. */
  time: number
  /** Tile path segment, e.g. `/v2/radar/<hash>`. */
  path: string
  /** True for short-range forecast frames (RainViewer `nowcast`). */
  nowcast: boolean
}

/** Resolved radar index: the tile host and the ordered frame list. */
export interface RadarData {
  host: string
  /** `past` frames followed by `nowcast` frames, in chronological order. */
  frames: RadarFrame[]
}

interface WeatherMapsFrame {
  time: number
  path: string
}

interface WeatherMapsResponse {
  host: string
  radar: {
    past: WeatherMapsFrame[]
    nowcast: WeatherMapsFrame[]
  }
}

/** Tile template for a frame: 256px tiles, colour scheme 2, smooth + snow on. */
export function radarTileUrl(host: string, frame: RadarFrame): string {
  return `${host}${frame.path}/256/{z}/{x}/{y}/2/1_1.png`
}

async function fetchRadar(signal: AbortSignal): Promise<RadarData> {
  const res = await fetch(WEATHER_MAPS_URL, { signal })
  if (!res.ok) throw new Error(`RainViewer index HTTP ${res.status}`)
  const json = (await res.json()) as WeatherMapsResponse
  const past = json.radar?.past ?? []
  const nowcast = json.radar?.nowcast ?? []
  const frames: RadarFrame[] = [
    ...past.map((f) => ({ time: f.time, path: f.path, nowcast: false })),
    ...nowcast.map((f) => ({ time: f.time, path: f.path, nowcast: true })),
  ]
  return { host: json.host, frames }
}

export const radarFramesOptions = () =>
  queryOptions({
    queryKey: ['radar-frames'] as const,
    queryFn: ({ signal }) => fetchRadar(signal),
    staleTime: TTL_MS,
    refetchInterval: TTL_MS,
  })

const STEP_MS = 600
const LAST_DWELL_MS = 1200

/**
 * Drives the active frame index for the radar animation. Advances one frame
 * every 600 ms while `playing`, dwells ~1.2 s on the last frame, then wraps to
 * the start. Paused holds the current frame. The index is always clamped to the
 * current frame count (the list changes as new frames publish).
 */
export function useRadarAnimation(
  frames: RadarFrame[],
  playing: boolean,
): number {
  const [index, setIndex] = useState(0)
  const count = frames.length

  // The scheduler reads the live index from a ref so the timer chain never
  // restarts on each tick (only when `playing` or the frame count changes).
  const indexRef = useRef(0)
  indexRef.current = count === 0 ? 0 : Math.min(index, count - 1)

  useEffect(() => {
    if (!playing || count <= 1) return
    let cancelled = false
    let timer: ReturnType<typeof setTimeout>

    const schedule = () => {
      const current = indexRef.current
      const last = count - 1
      const dwell = current >= last ? LAST_DWELL_MS : STEP_MS
      timer = setTimeout(() => {
        if (cancelled) return
        const next = current >= last ? 0 : current + 1
        indexRef.current = next
        setIndex(next)
        schedule()
      }, dwell)
    }

    schedule()
    return () => {
      cancelled = true
      clearTimeout(timer)
    }
  }, [playing, count])

  return indexRef.current
}
