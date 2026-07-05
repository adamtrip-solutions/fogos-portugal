import { createServerFn } from '@tanstack/react-start'
import { queryOptions } from '@tanstack/react-query'

// Surface wind field for the animated particle overlay, sourced from Open-Meteo
// (free, keyless, CORS `*`). Sampled on three regular 0.5° lat/lon grids — one
// per Portuguese domain — and returned as u/v component images ready to feed
// weatherlayers-gl's ParticleLayer. Meteorological direction (the bearing the
// wind blows FROM) is converted to eastward (u) / northward (v) components.
// The result is cached in-process for 60 minutes.

const OPEN_METEO_URL = 'https://api.open-meteo.com/v1/forecast'
const TTL_MS = 60 * 60 * 1000
const GRID_STEP = 0.5
const CHUNK_SIZE = 200

/** A wind component field over a lon/lat box; row 0 is the northernmost row. */
export interface WindField {
  /** `[west, south, east, north]` — the ParticleLayer `bounds`. */
  bounds: [number, number, number, number]
  width: number
  height: number
  /** Eastward component, m/s, row-major from north, lon ascending. */
  u: number[]
  /** Northward component, m/s, same order as `u`. */
  v: number[]
}

interface GridSpec {
  west: number
  east: number
  south: number
  north: number
}

// Regular 0.5° grids: continent 21×21, madeira 6×6, azores 17×11.
const GRIDS: GridSpec[] = [
  { west: -14, east: -4, south: 34, north: 44 },
  { west: -18, east: -15.5, south: 31.5, north: 34 },
  { west: -32, east: -24, south: 36, north: 41 },
]

interface GridPoint {
  lat: number
  lon: number
}

/** Rounds away float drift from repeated 0.5° additions. */
function round4(n: number): number {
  return Math.round(n * 1e4) / 1e4
}

/**
 * Grid points in image order: rows from north down to south, longitude
 * ascending within each row. Open-Meteo preserves request order, so the
 * response index lines up with this list.
 */
function gridPoints(grid: GridSpec): { points: GridPoint[]; width: number; height: number } {
  const width = Math.round((grid.east - grid.west) / GRID_STEP) + 1
  const height = Math.round((grid.north - grid.south) / GRID_STEP) + 1
  const points: GridPoint[] = []
  for (let row = 0; row < height; row++) {
    const lat = round4(grid.north - row * GRID_STEP)
    for (let col = 0; col < width; col++) {
      points.push({ lat, lon: round4(grid.west + col * GRID_STEP) })
    }
  }
  return { points, width, height }
}

interface OpenMeteoLocation {
  latitude: number
  longitude: number
  current?: {
    time: string
    wind_speed_10m: number | null
    wind_direction_10m: number | null
  }
}

/** Fetches ≤200 points; returns component pairs in request order. */
async function fetchChunk(
  points: GridPoint[],
): Promise<{ u: number; v: number }[]> {
  const params = new URLSearchParams({
    latitude: points.map((p) => p.lat).join(','),
    longitude: points.map((p) => p.lon).join(','),
    current: 'wind_speed_10m,wind_direction_10m',
    wind_speed_unit: 'ms',
  })
  const res = await fetch(`${OPEN_METEO_URL}?${params.toString()}`)
  if (!res.ok) throw new Error(`Open-Meteo HTTP ${res.status}`)
  const json = (await res.json()) as OpenMeteoLocation | OpenMeteoLocation[]
  const locations = Array.isArray(json) ? json : [json]
  return locations.map((loc) => {
    const speed = loc.current?.wind_speed_10m ?? 0
    const dir = loc.current?.wind_direction_10m ?? 0
    const rad = (dir * Math.PI) / 180
    return { u: -speed * Math.sin(rad), v: -speed * Math.cos(rad) }
  })
}

async function resolveGrid(grid: GridSpec): Promise<WindField> {
  const { points, width, height } = gridPoints(grid)

  const chunks: { u: number; v: number }[] = []
  for (let i = 0; i < points.length; i += CHUNK_SIZE) {
    chunks.push(...(await fetchChunk(points.slice(i, i + CHUNK_SIZE))))
  }

  const u = chunks.map((c) => c.u)
  const v = chunks.map((c) => c.v)
  return {
    bounds: [grid.west, grid.south, grid.east, grid.north],
    width,
    height,
    u,
    v,
  }
}

let cache: { at: number; value: WindField[] } | null = null

export const getWindField = createServerFn({ method: 'GET' }).handler(
  async (): Promise<WindField[]> => {
    if (cache && Date.now() - cache.at < TTL_MS) return cache.value
    const value = await Promise.all(GRIDS.map(resolveGrid))
    cache = { at: Date.now(), value }
    return value
  },
)

export const windFieldOptions = ({ enabled }: { enabled: boolean }) =>
  queryOptions({
    queryKey: ['wind-field'] as const,
    queryFn: () => getWindField(),
    staleTime: TTL_MS,
    refetchInterval: TTL_MS,
    enabled,
  })
