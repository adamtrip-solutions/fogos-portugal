import { createServerFn } from '@tanstack/react-start'
import { queryOptions } from '@tanstack/react-query'

// IPMA AROME model runs publish with lag and the advertised GetCapabilities
// default is unreliable, so the live reference time is discovered by probing
// candidate runs (newest first) with a tiny GetMap and keeping the first that
// returns a real PNG. The result is cached in-process for 15 minutes.

export interface WeatherAvailability {
  /** Published model run (`YYYY-MM-DDTHH:MM`, UTC) or null when none is live. */
  referenceTime: string | null
  /** Forecast valid time within [ref, ref+48h] (`YYYY-MM-DDTHH:MM`, UTC). */
  time: string | null
  /** Domains confirmed live for this run. Continent is always present. */
  regions: string[]
}

const IPMA_WMS = 'https://mf2.ipma.pt/services/'
const TTL_MS = 15 * 60 * 1000
const WINDOW_MS = 48 * 60 * 60 * 1000

// EPSG:3857 bounding boxes per domain (from the verified probe examples).
const CONTINENT_BBOX = '-1090000,4400000,-660000,5200000'
const MADEIRA_BBOX = '-2113960,3630450,-1645970,4129960'
const AZORES_BBOX = '-3560000,4260000,-2710000,5090000'

const PROBE_LAYER_BASE = 'arome.2m.temperature'

let cache: { at: number; value: WeatherAvailability } | null = null

const HOUR_MS = 60 * 60 * 1000

const pad = (n: number): string => String(n).padStart(2, '0')

/** Format a Date to `YYYY-MM-DDTHH:MM` in UTC (minute precision). */
function fmtUtc(d: Date): string {
  return (
    `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}` +
    `T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`
  )
}

/**
 * Forecast time used to probe a run. AROME publishes steps starting at ref+1h;
 * `time == reference_time` (step 0) is NOT published and 404s, so probing at
 * the run hour would wrongly reject a live run. ref+1h is a real step.
 */
function probeTimeFor(referenceTime: string): string {
  return fmtUtc(new Date(new Date(`${referenceTime}:00Z`).getTime() + HOUR_MS))
}

/** Candidate model runs, newest first: today/yesterday 12:00 & 00:00, +DBY 12:00. */
function candidateRuns(now: Date): string[] {
  const DAY = 24 * 60 * 60 * 1000
  const runAt = (base: Date, hour: number) =>
    fmtUtc(
      new Date(
        Date.UTC(
          base.getUTCFullYear(),
          base.getUTCMonth(),
          base.getUTCDate(),
          hour,
        ),
      ),
    )
  const today = now
  const yesterday = new Date(now.getTime() - DAY)
  const dayBefore = new Date(now.getTime() - 2 * DAY)
  return [
    runAt(today, 12),
    runAt(today, 0),
    runAt(yesterday, 12),
    runAt(yesterday, 0),
    runAt(dayBefore, 12),
  ]
}

/** Tiny GetMap probe: true when IPMA answers with a real PNG. */
async function probe(
  layer: string,
  bbox: string,
  referenceTime: string,
): Promise<boolean> {
  const params = new URLSearchParams({
    service: 'WMS',
    version: '1.3.0',
    request: 'GetMap',
    layers: layer,
    styles: '',
    format: 'image/png',
    transparent: 'true',
    crs: 'EPSG:3857',
    bbox,
    width: '16',
    height: '16',
    time: probeTimeFor(referenceTime),
    reference_time: referenceTime,
  })
  try {
    const res = await fetch(`${IPMA_WMS}?${params.toString()}`)
    if (!res.ok) return false
    return (res.headers.get('content-type') ?? '').includes('image/png')
  } catch {
    return false
  }
}

async function resolveAvailability(): Promise<WeatherAvailability> {
  const now = new Date()

  let referenceTime: string | null = null
  for (const run of candidateRuns(now)) {
    if (await probe(`${PROBE_LAYER_BASE}.continent`, CONTINENT_BBOX, run)) {
      referenceTime = run
      break
    }
  }

  if (referenceTime == null) {
    // No AROME run is live — the UI still offers the fire risk layer.
    return { referenceTime: null, time: null, regions: ['continent'] }
  }

  // Continent is live by construction; probe the island domains once.
  const regions: string[] = ['continent']
  const [madeiraLive, azoresLive] = await Promise.all([
    probe(`${PROBE_LAYER_BASE}.madeira`, MADEIRA_BBOX, referenceTime),
    probe(`${PROBE_LAYER_BASE}.azores`, AZORES_BBOX, referenceTime),
  ])
  if (madeiraLive) regions.push('madeira')
  if (azoresLive) regions.push('azores')

  // Forecast time: current UTC hour, floored, clamped into [ref+1h, ref+48h].
  // The lower bound is ref+1h (not ref) because step 0 is not published.
  const refMs = new Date(`${referenceTime}:00Z`).getTime()
  const flooredHour = Date.UTC(
    now.getUTCFullYear(),
    now.getUTCMonth(),
    now.getUTCDate(),
    now.getUTCHours(),
  )
  const clamped = Math.min(
    Math.max(flooredHour, refMs + HOUR_MS),
    refMs + WINDOW_MS,
  )
  const time = fmtUtc(new Date(clamped))

  return { referenceTime, time, regions }
}

export const getWeatherAvailability = createServerFn({ method: 'GET' }).handler(
  async (): Promise<WeatherAvailability> => {
    if (cache && Date.now() - cache.at < TTL_MS) return cache.value
    const value = await resolveAvailability()
    cache = { at: Date.now(), value }
    return value
  },
)

export const weatherAvailabilityOptions = () =>
  queryOptions({
    queryKey: ['weather-availability'] as const,
    queryFn: () => getWeatherAvailability(),
    staleTime: TTL_MS,
    refetchInterval: TTL_MS,
  })
