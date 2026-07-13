// Status buckets, palette colors, and pt-PT labels shared by every surface (map
// dots, sheet badges, legends). Single source of truth so all agree on grouping
// and color. No React / RN imports.

const ACTIVE_STATUS_CODES = new Set([3, 4, 5, 6])

export function isActiveStatus(code: number): boolean {
  return ACTIVE_STATUS_CODES.has(code)
}

/**
 * Still-ongoing statuses: active (3–6) plus Em Resolução (7) and Vigilância (9).
 * These show on the map regardless of age; only finished fires (Conclusão,
 * Encerrada, close-out 13, falso alarme/alerta) get time-windowed out.
 */
export function isOngoingStatus(code: number): boolean {
  return ACTIVE_STATUS_CODES.has(code) || code === 7 || code === 9
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

/**
 * Status codes that map to each bucket — the inverse of {@link statusBucket},
 * kept in lockstep with it. Translates a set of selected buckets into the
 * `statusCodes` filter the API expects (see `buildIncidentsFilter`).
 */
export const STATUS_BUCKET_CODES: Record<StatusBucket, number[]> = {
  dispatch: [3, 4],
  ongoing: [5, 6],
  resolving: [7],
  vigilancia: [9],
  done: [8, 10, 11, 12, 13],
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
