import type { IncidentListItem } from './types.ts'

const ACTIVE_STATUS_CODES = new Set([3, 4, 5, 6])

export function isActiveStatus(code: number): boolean {
  return ACTIVE_STATUS_CODES.has(code)
}

/**
 * Still-ongoing statuses: active (3–6) plus Em Resolução (7) and
 * Vigilância (9). These show on the map regardless of age; only finished
 * fires (Conclusão, Encerrada, feed-drop close-out 13, falso alarme/alerta)
 * get time-windowed.
 */
export function isOngoingStatus(code: number): boolean {
  return ACTIVE_STATUS_CODES.has(code) || code === 7 || code === 9
}

/** API status colors are hex without a leading `#`. */
export function colorWithHash(color: string): string {
  return color.startsWith('#') ? color : `#${color}`
}

const STATUS_RED = '#B81E1F' // em curso
const STATUS_ORANGE = '#FF6E02' // despacho
const STATUS_GREEN = '#6ABF59' // resolução / vigilância / encerrada
const STATUS_GRAY = '#BDBDBD' // conclusão / falso alarme

/** Timeline entries carry only a status code, so map it to the palette. */
export function statusColorForCode(code: number): string {
  if (code === 3 || code === 4) return STATUS_ORANGE
  if (code === 5 || code === 6) return STATUS_RED
  // Backend catalog: 7 (Em Resolução), 9 (Vigilância), 10 (Encerrada) and
  // 13 (Encerrada sem atualização) are green; 8 (Conclusão) and 11/12 (falso
  // alarme/alerta) are gray.
  if (code === 7 || code === 9 || code === 10 || code === 13) return STATUS_GREEN
  return STATUS_GRAY
}

/**
 * Coarse status grouping shared by the map badges and the legend, so both
 * agree on icon + color. Single source of truth for the 4 buckets.
 */
export const STATUS_BUCKETS = [
  'dispatch',
  'ongoing',
  'resolving',
  'done',
] as const
export type StatusBucket = (typeof STATUS_BUCKETS)[number]

/** PT-PT labels for each bucket (filter UIs, chips). */
export const STATUS_BUCKET_LABEL: Record<StatusBucket, string> = {
  dispatch: 'Despacho',
  ongoing: 'Em curso',
  resolving: 'Em resolução',
  done: 'Concluído',
}

/**
 * Status codes that map to each bucket — the inverse of `statusBucket`, kept in
 * lockstep with it. Used to translate a set of selected buckets into the
 * `statusCodes` filter the API expects.
 */
export const STATUS_BUCKET_CODES: Record<StatusBucket, number[]> = {
  dispatch: [3, 4],
  ongoing: [5, 6],
  resolving: [7, 9, 10, 13],
  done: [8, 11, 12],
}

export function statusBucket(code: number): StatusBucket {
  if (code === 3 || code === 4) return 'dispatch' // laranja #FF6E02
  if (code === 5 || code === 6) return 'ongoing' // vermelho #B81E1F
  // 13 (Encerrada sem atualização) shares Encerrada's green family; it's finished
  // (not ongoing, so it time-windows out like 10) but reads green, not gray.
  if (code === 7 || code === 9 || code === 10 || code === 13) return 'resolving' // verde #6ABF59
  return 'done' // cinzento #BDBDBD (8, 11, 12, unknown)
}

/** Bucket → palette color; mirrors the STATUS_* constants above. */
export const STATUS_BUCKET_COLOR: Record<StatusBucket, string> = {
  dispatch: STATUS_ORANGE,
  ongoing: STATUS_RED,
  resolving: STATUS_GREEN,
  done: STATUS_GRAY,
}

/** Gray badges need dark text; every other status color reads on white. */
export function badgeNeedsDarkText(color: string): boolean {
  return colorWithHash(color).toUpperCase() === STATUS_GRAY
}

const rtf = new Intl.RelativeTimeFormat('pt', { numeric: 'auto' })

const DIVISIONS: Array<{ amount: number; unit: Intl.RelativeTimeFormatUnit }> = [
  { amount: 60, unit: 'second' },
  { amount: 60, unit: 'minute' },
  { amount: 24, unit: 'hour' },
  { amount: 30, unit: 'day' },
  { amount: 12, unit: 'month' },
  { amount: Number.POSITIVE_INFINITY, unit: 'year' },
]

/** e.g. "há 2 horas". */
export function formatRelative(iso: string): string {
  let duration = (Date.parse(iso) - Date.now()) / 1000
  for (const division of DIVISIONS) {
    if (Math.abs(duration) < division.amount) {
      return rtf.format(Math.round(duration), division.unit)
    }
    duration /= division.amount
  }
  return rtf.format(Math.round(duration), 'year')
}

const absoluteFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

/** e.g. "4 jul., 03:50". */
export function formatAbsolute(iso: string): string {
  return absoluteFmt.format(new Date(iso))
}

const timeFmt = new Intl.DateTimeFormat('pt-PT', {
  hour: '2-digit',
  minute: '2-digit',
})
const dayMonthFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
})

/** e.g. "03:50 · 4 jul.". */
export function formatTimelineStamp(iso: string): string {
  const d = new Date(iso)
  return `${timeFmt.format(d)} · ${dayMonthFmt.format(d)}`
}

/** Join freguesia · concelho · district, skipping empties and consecutive repeats. */
export function locationParts(
  freguesia: string | null,
  concelho: string | null,
  district: string | null,
): string {
  const parts: string[] = []
  for (const raw of [freguesia, concelho, district]) {
    const value = raw?.trim()
    if (!value) continue
    if (parts.some((p) => p.toLowerCase() === value.toLowerCase())) continue
    parts.push(value)
  }
  return parts.join(' · ')
}

/**
 * Finished fires only show for this long after their last update; ongoing
 * ones (see isOngoingStatus) always show. "Só ativos" narrows to active fires.
 */
export const WINDOW_HOURS = 12

export function countLabel(count: number, activeOnly = false): string {
  if (activeOnly) {
    if (count <= 0) return 'Sem incêndios ativos'
    if (count === 1) return '1 incêndio ativo'
    return `${count} incêndios ativos`
  }
  // The default view mixes ongoing fires of any age with recently finished
  // ones, so a time-window label would be misleading — plain count only.
  if (count <= 0) return 'Sem incêndios'
  if (count === 1) return '1 incêndio'
  return `${count} incêndios`
}

/** Resource counts use -1 as an "unknown" sentinel; treat <= 0 as absent. */
export function hasResource(value: number): boolean {
  return value > 0
}

const hectaresFmt = new Intl.NumberFormat('pt-PT', {
  maximumFractionDigits: 1,
})

/** e.g. "11 985,7 ha". */
export function formatHectares(value: number): string {
  return `${hectaresFmt.format(value)} ha`
}

export function incidentTitle(item: {
  natureza: IncidentListItem['natureza']
}): string {
  return item.natureza?.trim() || 'Incêndio'
}

/**
 * Human duration in European Portuguese, e.g. "1 h 23 min", "45 min", "2 h".
 * Sub-minute durations collapse to "< 1 min". Negative inputs clamp to 0.
 */
export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  if (h > 0 && m > 0) return `${h} h ${m} min`
  if (h > 0) return `${h} h`
  if (m > 0) return `${m} min`
  return '< 1 min'
}

/** PT labels for the critical-conditions machine keys (WP1 signals). */
export const CRITICAL_REASON_LABELS: Record<string, string> = {
  TEMP_ABOVE_30: 'Temperatura > 30 °C',
  HUMIDITY_BELOW_30: 'Humidade < 30%',
  WIND_ABOVE_30: 'Vento > 30 km/h',
  RISK_MAXIMUM: 'Risco máximo',
  HEAT_WAVE: 'Onda de calor',
}

/** Falls back to the raw key when a reason is not in the catalog. */
export function criticalReasonLabel(key: string): string {
  return CRITICAL_REASON_LABELS[key] ?? key
}

/**
 * Compass bearing (degrees, clockwise from North) for a wind-direction token,
 * or null when unrecognized. Covers the 8/16-point English + Portuguese
 * abbreviations the API emits (N, NE, E, SE, S, SO/SW, O/W, NO/NW, …).
 */
export function compassBearing(direction: string | null | undefined): number | null {
  if (!direction) return null
  const key = direction.trim().toUpperCase().replace(/[^NSEWO]/g, '')
  const table: Record<string, number> = {
    N: 0,
    NNE: 22.5,
    NE: 45,
    ENE: 67.5,
    E: 90,
    ESE: 112.5,
    SE: 135,
    SSE: 157.5,
    S: 180,
    SSO: 202.5,
    SSW: 202.5,
    SO: 225,
    SW: 225,
    OSO: 247.5,
    WSW: 247.5,
    O: 270,
    W: 270,
    ONO: 292.5,
    WNW: 292.5,
    NO: 315,
    NW: 315,
    NNO: 337.5,
    NNW: 337.5,
  }
  return key in table ? table[key] : null
}
