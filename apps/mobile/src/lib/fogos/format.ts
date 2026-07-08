// Status/bucket presentation helpers — ported from
// apps/web/src/lib/fogos/format.ts (map + sheet subset). pt-PT throughout.

const ACTIVE_STATUS_CODES = new Set([3, 4, 5, 6])

export function isActiveStatus(code: number): boolean {
  return ACTIVE_STATUS_CODES.has(code)
}

const STATUS_RED = '#B81E1F' // em curso
const STATUS_ORANGE = '#FF6E02' // despacho
const STATUS_GREEN = '#6ABF59' // em resolução (dominado, ainda no terreno)
const STATUS_BLUE = '#1E88E5' // vigilância (extinto, sob vigilância)
const STATUS_GRAY = '#BDBDBD' // concluído / falso alarme / encerrada

/**
 * Coarse status grouping shared by the map dots and the sheet badge, so both
 * agree on color. Single source of truth for the 5 buckets.
 */
export const STATUS_BUCKETS = [
  'dispatch',
  'ongoing',
  'resolving',
  'vigilancia',
  'done',
] as const
export type StatusBucket = (typeof STATUS_BUCKETS)[number]

/** PT-PT labels for each bucket. */
export const STATUS_BUCKET_LABEL: Record<StatusBucket, string> = {
  dispatch: 'Despacho',
  ongoing: 'Em curso',
  resolving: 'Em resolução',
  vigilancia: 'Vigilância',
  done: 'Concluído',
}

export function statusBucket(code: number): StatusBucket {
  if (code === 3 || code === 4) return 'dispatch' // laranja #FF6E02
  if (code === 5 || code === 6) return 'ongoing' // vermelho #B81E1F
  if (code === 7) return 'resolving' // verde #6ABF59 — dominado, ainda no terreno
  if (code === 9) return 'vigilancia' // azul #1E88E5 — extinto, sob vigilância
  // 8/10/11/12/13 (conclusão, encerrada, falso alarme/alerta) are finished:
  // gray, and time-windowed out on the map.
  return 'done'
}

/** Bucket → palette color; mirrors the STATUS_* constants above. */
export const STATUS_BUCKET_COLOR: Record<StatusBucket, string> = {
  dispatch: STATUS_ORANGE,
  ongoing: STATUS_RED,
  resolving: STATUS_GREEN,
  vigilancia: STATUS_BLUE,
  done: STATUS_GRAY,
}

/**
 * Presentation color for ANY status rendering (map dots, sheet badge): the
 * bucket palette, never the API's `status.color`, so every surface agrees.
 */
export function statusColorForCode(code: number): string {
  return STATUS_BUCKET_COLOR[statusBucket(code)]
}

/** Gray badges need dark text; every other status color reads on white. */
export function badgeNeedsDarkText(color: string): boolean {
  return color.toUpperCase() === STATUS_GRAY
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

/** Join freguesia · concelho · district, skipping empties and repeats. */
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

/** Resource counts use -1 as an "unknown" sentinel; treat <= 0 as absent. */
export function hasResource(value: number): boolean {
  return value > 0
}

export function incidentTitle(item: { natureza: string | null }): string {
  return item.natureza?.trim() || 'Incêndio'
}
