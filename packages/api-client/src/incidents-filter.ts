// Incidents-table filter builder + district list — ported verbatim from the web
// app (apps/web/src/lib/fogos/api.ts). The `window → after` mapping, the
// `buckets → statusCodes` translation, and the district list are all API filter
// values, so they must match web EXACTLY. Backs the mobile Ocorrências screen
// (and the web /ocorrencias route once it adopts this package).

import { STATUS_BUCKETS, STATUS_BUCKET_CODES } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'

import { lisbonDateDaysAgo } from './time'

/** Time window for the incidents table; maps to an `after` Lisbon-day cutoff. */
export type IncidentsWindow = '1d' | '3d' | '7d' | '30d' | 'all'

const WINDOW_DAYS: Record<Exclude<IncidentsWindow, 'all'>, number> = {
  '1d': 0, // "Hoje" — today's Lisbon calendar day only, not a rolling 24h
  '3d': 3,
  '7d': 7,
  '30d': 30,
}

/** The subset of `IncidentFilter` the table page drives (fire-only by default). */
export interface IncidentsFilter {
  after?: string
  statusCodes?: number[]
  district?: string
}

/**
 * Resolve the page's filter facets into the GraphQL `IncidentFilter`. Buckets
 * only constrain `statusCodes` when a strict subset is selected (all five = no
 * constraint); `kind`/`all` are never set so the default fire-only view stands.
 * Verbatim port of web's `buildIncidentsFilter`.
 */
export function buildIncidentsFilter(params: {
  window: IncidentsWindow
  buckets: readonly StatusBucket[]
  district?: string
}): IncidentsFilter {
  const filter: IncidentsFilter = {}
  if (params.window !== 'all') {
    filter.after = lisbonDateDaysAgo(WINDOW_DAYS[params.window])
  }
  const buckets = params.buckets
  if (buckets.length > 0 && buckets.length < STATUS_BUCKETS.length) {
    filter.statusCodes = buckets.flatMap((b) => STATUS_BUCKET_CODES[b])
  }
  if (params.district) filter.district = params.district
  return filter
}

/**
 * The 18 mainland + 11 island districts from backend/dev/seed/locations.json
 * (level-1 rows), sorted pt-PT. Copied verbatim from web's DISTRICTS list —
 * these are API `district` filter values, so they must match EXACTLY.
 * Islands surface through this filter, same as web.
 */
export const INCIDENT_DISTRICTS = [
  'Aveiro',
  'Beja',
  'Braga',
  'Bragança',
  'Castelo Branco',
  'Coimbra',
  'Évora',
  'Faro',
  'Guarda',
  'Ilha da Madeira',
  'Ilha das Flores',
  'Ilha de Porto Santo',
  'Ilha de Santa Maria',
  'Ilha de São Jorge',
  'Ilha de São Miguel',
  'Ilha do Corvo',
  'Ilha do Faial',
  'Ilha do Pico',
  'Ilha Graciosa',
  'Ilha Terceira',
  'Leiria',
  'Lisboa',
  'Portalegre',
  'Porto',
  'Santarém',
  'Setúbal',
  'Viana do Castelo',
  'Vila Real',
  'Viseu',
] as const
