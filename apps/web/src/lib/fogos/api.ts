import { createServerFn } from '@tanstack/react-start'
import { getRequestHeader } from '@tanstack/react-start/server'
import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query'
import type { FeatureCollection, Geometry } from 'geojson'

import { STATUS_BUCKETS, STATUS_BUCKET_CODES } from './format.ts'
import type { StatusBucket } from './format.ts'
import { concelhoByDico } from './concelhos.ts'
import type {
  ConcelhoProfile,
  IncidentDetail,
  IncidentListItem,
  RegisteredDevice,
  SeasonStats,
  SituationReport,
  WeatherWarning,
} from './types.ts'
// Type-only reuse of the account shapes — `import type` is erased at build time,
// so this never pulls the Clerk server module into the anonymous X-API-Key path.
import type {
  AlertSubscriptionInput,
  OwnedAlertSubscription,
} from './account-api.ts'

const ACTIVE_INCIDENTS_QUERY = /* GraphQL */ `
  query Active {
    activeIncidents {
      id
      location
      district
      concelho
      freguesia
      coordinates { latitude longitude }
      status { code label color }
      kind
      natureza
      important
      occurredAt
      updatedAt
      statusChangedAt
      resources { man terrain aerial aquatic }
      signals { escalating rekindle criticalConditions demobilizedSince }
    }
  }
`

const INCIDENT_DETAIL_QUERY = /* GraphQL */ `
  query Detail($id: ID!) {
    incident(id: $id) {
      id
      location
      detailLocation
      district
      concelho
      freguesia
      extra
      coordinates { latitude longitude }
      status { code label color }
      kind
      natureza
      important
      active
      occurredAt
      updatedAt
      resources { man terrain aerial aquatic heliFight heliCoord planeFight }
      weather { stationName distanceKm at temperature humidity windSpeedKmh windDirection }
      statusHistory { at code label }
      history(first: 500) { at man terrain aerial }
      icnf { causeType cause burnArea { total } updatedAt }
      photos { id publicUrl width height takenAt gps { latitude longitude } }
      signals {
        escalating
        escalationDetectedAt
        peakAssets
        rekindle
        rekindleOfId
        rekindleDetectedAt
        criticalConditions
        criticalReasons
        conditionsEvaluatedAt
      }
      responseTimes {
        dispatchToArrivalSeconds
        arrivalToControlSeconds
        controlToConclusionSeconds
        totalSeconds
      }
      aircraft {
        icao
        registration
        name
        kind
        active
        firstSeenAt
        lastSeenAt
        samples
      }
      kmlHistory { id vost capturedAt sizeBytes }
      hotspots {
        viirs { position { latitude longitude } acquiredAt brightness confidence }
        modis { position { latitude longitude } acquiredAt brightness confidence }
      }
    }
  }
`

const RECENT_INCIDENTS_QUERY = /* GraphQL */ `
  query Recent($filter: IncidentFilter, $cursor: String) {
    incidents(filter: $filter, first: 100, after: $cursor) {
      pageInfo { hasNextPage endCursor }
      nodes {
        id
        location
        district
        concelho
        freguesia
        coordinates { latitude longitude }
        status { code label color }
        kind
        natureza
        important
        occurredAt
        updatedAt
        statusChangedAt
        resources { man terrain aerial aquatic }
        signals { escalating rekindle criticalConditions demobilizedSince }
      }
    }
  }
`

// ── demobilizedSince graceful degradation ─────────────────────────────────────
//
// The list feeds above select `signals.demobilizedSince`, a field a freshly
// deployed API has but an OLDER one (a rollback, or a preview pointed at a stale
// API) does not. GraphQL validates the whole document up front, so an unknown
// field hard-fails the ENTIRE query — which would blank the map on every poll.
// Fallback documents drop just that field (the map predicate treats an absent
// demobilizedSince as null anyway). We try the full document first; on the
// schema-validation error naming the field we retry once with the fallback and
// latch a module-level flag so subsequent polls skip straight to the fallback
// (no repeated double request until the process restarts against a newer API).
const ACTIVE_INCIDENTS_QUERY_FALLBACK = ACTIVE_INCIDENTS_QUERY.replace(
  ' demobilizedSince',
  '',
)
const RECENT_INCIDENTS_QUERY_FALLBACK = RECENT_INCIDENTS_QUERY.replace(
  ' demobilizedSince',
  '',
)

let demobilizedSinceUnsupported = false

/** Whether a GraphQL error message points at the unknown `demobilizedSince` field. */
function isDemobilizedSinceUnknown(error: unknown): boolean {
  return error instanceof FogosApiError && /demobilizedSince/i.test(error.message)
}

/**
 * Run a list-feed document, degrading gracefully when the deployed API predates
 * the `demobilizedSince` field: retry once without it and latch so later calls
 * use the fallback directly.
 */
async function graphqlListFeed<T>(
  primary: string,
  fallback: string,
  variables?: Record<string, unknown>,
): Promise<T> {
  if (demobilizedSinceUnsupported) {
    return graphql<T>(fallback, variables)
  }
  try {
    return await graphql<T>(primary, variables)
  } catch (error) {
    if (isDemobilizedSinceUnknown(error)) {
      demobilizedSinceUnsupported = true
      return graphql<T>(fallback, variables)
    }
    throw error
  }
}

interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string; extensions?: { code?: string } }>
}

/** GraphQL error carrying the API's error `code` so callers can map it to pt-PT copy. */
export class FogosApiError extends Error {
  readonly code?: string
  constructor(message: string, code?: string) {
    super(message)
    this.name = 'FogosApiError'
    this.code = code
  }
}

// Server-only: attaches the first-party API key (minted via AdminCli, hashed in
// api_clients) so SSR calls are rate-limited as first-party rather than anonymous.
// The header is omitted entirely when FOGOS_API_KEY is unset — dev without a key
// keeps working (RateLimit is disabled in the API's Development env). Never read
// this from browser code: process.env is server-only and this module only runs
// inside createServerFn handlers.
function apiHeaders(extra?: Record<string, string>): Record<string, string> {
  const headers: Record<string, string> = { ...extra }
  const key = process.env.FOGOS_API_KEY
  if (key) headers['X-API-Key'] = key
  return headers
}

async function graphql<T>(
  query: string,
  variables?: Record<string, unknown>,
  extraHeaders?: Record<string, string>,
): Promise<T> {
  const endpoint = `${process.env.FOGOS_API_URL ?? 'http://localhost:5077'}/graphql`

  const res = await fetch(endpoint, {
    method: 'POST',
    headers: apiHeaders({ 'Content-Type': 'application/json', ...extraHeaders }),
    body: JSON.stringify({ query, variables }),
  })

  if (!res.ok) {
    throw new FogosApiError(`Fogos API responded with ${res.status}`)
  }

  const json = (await res.json()) as GraphQLResponse<T>

  if (json.errors && json.errors.length > 0) {
    const first = json.errors[0]
    throw new FogosApiError(
      json.errors.map((e) => e.message).join('; '),
      first.extensions?.code,
    )
  }

  if (!json.data) {
    throw new FogosApiError('Fogos API returned no data')
  }

  return json.data
}

// ── Client-IP forwarding (mutating device/subscription calls only) ────────────
//
// Server fns proxy browser → web container → API, so without this every visitor
// would present the web container's single IP and the API's per-IP abuse gates
// (DeviceRegistrationGate, AlertSubscriptionGate) would throttle the whole site
// at once. We forward the REAL browser IP so each visitor is gated on their own.
//
// ClientIpResolver (backend/src/Fogos.Api/Auth/ClientIpResolver.cs) trusts the
// configured edge header `CF-Connecting-IP` above everything else — Cloudflare
// overwrites it at the edge, so a client cannot forge it past there. Prod sits
// behind a Cloudflare tunnel, so the incoming request to THIS container already
// carries `cf-connecting-ip`; we fall back to the first hop of `x-forwarded-for`
// (the original client) when it's absent.
//
// TRUST: the API is never reachable directly by browsers (no CORS, internal
// only), so the only party that can set `CF-Connecting-IP` on a call reaching it
// is this trusted server hop — exactly what ClientIpResolver assumes.
function forwardedClientIpHeaders(): Record<string, string> {
  const cf = getRequestHeader('cf-connecting-ip')?.trim()
  if (cf) return { 'CF-Connecting-IP': cf }
  const first = getRequestHeader('x-forwarded-for')?.split(',')[0]?.trim()
  if (first) return { 'CF-Connecting-IP': first }
  return {}
}

// GraphQL calls run only on the server — the API has no CORS.
export const fetchActiveIncidents = createServerFn({ method: 'GET' }).handler(
  async () => {
    const data = await graphqlListFeed<{ activeIncidents: IncidentListItem[] }>(
      ACTIVE_INCIDENTS_QUERY,
      ACTIVE_INCIDENTS_QUERY_FALLBACK,
    )
    return data.activeIncidents
  },
)

interface RecentIncidentsPage {
  incidents: {
    pageInfo: { hasNextPage: boolean; endCursor: string | null }
    nodes: IncidentListItem[]
  }
}

/** Lisbon calendar date (YYYY-MM-DD) for a given instant. */
const lisbonDateFmt = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Lisbon',
})

/** Lisbon calendar day (YYYY-MM-DD) `days` before now — the `after` cutoff. */
function lisbonDateDaysAgo(days: number): string {
  return lisbonDateFmt.format(new Date(Date.now() - days * 24 * 60 * 60 * 1000))
}

// Recent incidents: fires whose record CHANGED on/after (now − 3 days)'s Lisbon
// calendar day (updatedAt, not occurredAt). This keeps long-running fires still
// in resolução/vigilância visible for as long as anything is happening to them
// — they only surface through this query, since the activeIncidents feed covers
// codes 3–6 only — and lets closed fires drop out of the fetch 3 days after
// their last change. The map's display window for finished fires keys on
// statusChangedAt (conclusion time), NOT updatedAt — the ICNF enrichment job
// bulk-bumps updatedAt on hundreds of concluded fires per sweep, and keying
// visibility on it flooded the map with long-dead gray dots (2026-07-06).
// Fetch breadth here stays updatedAt-based on purpose; display decides.
//
// The connection sorts by occurredAt desc while this filter is updatedAt-based,
// so under heavy load (>500 touched records in the window) the page cap would
// truncate the OLDEST-STARTED fires first — exactly the long-runners. The
// second fetch below pins recently-touched statusCodes 7/9 (Em Resolução/
// Vigilância) so those can never be truncation victims; the merge dedupes by
// id. The tail keeps the same updatedAfter bound ON PURPOSE: nothing ever
// closes a 7/9 fire that ANEPC drops from the feed (close-out only sweeps
// active 3–6), so an unbounded tail would accumulate stale green dots forever.
async function fetchIncidentFeed(
  filter: Record<string, unknown>,
  maxPages: number,
): Promise<IncidentListItem[]> {
  const nodes: IncidentListItem[] = []
  let cursor: string | null = null

  for (let page = 0; page < maxPages; page++) {
    const data: RecentIncidentsPage = await graphqlListFeed<RecentIncidentsPage>(
      RECENT_INCIDENTS_QUERY,
      RECENT_INCIDENTS_QUERY_FALLBACK,
      { filter, cursor },
    )
    nodes.push(...data.incidents.nodes)
    if (!data.incidents.pageInfo.hasNextPage) break
    cursor = data.incidents.pageInfo.endCursor
  }

  return nodes
}

export const fetchRecentIncidents = createServerFn({ method: 'GET' }).handler(
  async () => {
    const after = lisbonDateDaysAgo(3)
    const [changed, ongoingTail] = await Promise.all([
      fetchIncidentFeed({ updatedAfter: after }, 5),
      fetchIncidentFeed({ updatedAfter: after, statusCodes: [7, 9] }, 2),
    ])

    const byId = new Map<string, IncidentListItem>()
    for (const inc of [...changed, ...ongoingTail]) byId.set(inc.id, inc)
    return [...byId.values()]
  },
)

export const fetchIncident = createServerFn({ method: 'GET' })
  .validator((id: string) => id)
  .handler(async ({ data: id }) => {
    const data = await graphql<{ incident: IncidentDetail | null }>(
      INCIDENT_DETAIL_QUERY,
      { id },
    )
    return data.incident
  })

export const activeIncidentsQuery = () =>
  queryOptions({
    queryKey: ['active-incidents'] as const,
    queryFn: () => fetchActiveIncidents(),
    refetchInterval: 60_000,
    staleTime: 30_000,
    refetchOnWindowFocus: true,
  })

export const recentIncidentsQuery = () =>
  queryOptions({
    queryKey: ['recent-incidents'] as const,
    queryFn: () => fetchRecentIncidents(),
    refetchInterval: 60_000,
    staleTime: 30_000,
  })

export const incidentQuery = (id: string) =>
  queryOptions({
    queryKey: ['incident', id] as const,
    queryFn: () => fetchIncident({ data: id }),
    refetchInterval: 60_000,
  })

// Raw KML for one perimeter version. Server-only: the REST endpoint has no CORS
// and the versioned snapshot is immutable, so it fetches once and caches forever.
export const fetchKmlVersion = createServerFn({ method: 'GET' })
  .validator((input: { id: string; versionId: string }) => input)
  .handler(async ({ data }) => {
    const base = process.env.FOGOS_API_URL ?? 'http://localhost:5077'
    const res = await fetch(
      `${base}/v3/incidents/${data.id}/kml-versions/${data.versionId}`,
      { headers: apiHeaders() },
    )
    if (!res.ok) {
      throw new Error(`Fogos API responded with ${res.status}`)
    }
    return await res.text()
  })

export const kmlVersionQuery = (id: string, versionId: string) =>
  queryOptions({
    queryKey: ['kml-version', id, versionId] as const,
    queryFn: () => fetchKmlVersion({ data: { id, versionId } }),
    // Versioned snapshots never change.
    staleTime: Number.POSITIVE_INFINITY,
    gcTime: 10 * 60_000,
  })

// ── Season statistics (WP3) ──────────────────────────────────────────────────

// The list-item subset reused by the concelho profile's active incidents.
const INCIDENT_LIST_FIELDS = /* GraphQL */ `
  id
  location
  district
  concelho
  freguesia
  coordinates { latitude longitude }
  status { code label color }
  kind
  natureza
  important
  occurredAt
  updatedAt
  statusChangedAt
  resources { man terrain aerial aquatic }
  signals { escalating rekindle criticalConditions }
`

const SEASON_STATS_QUERY = /* GraphQL */ `
  query Season($year: Int!, $prevYear: Int!) {
    stats {
      activeFires
      today
      yesterday
      week
      burnAreaTotalHa(year: $year)
      totals { man terrain aerial total at }
      current: ignitionsByDay(year: $year) { date count }
      previous: ignitionsByDay(year: $prevYear) { date count }
      burnAreaCumulative(year: $year) { date totalHa }
      causeBreakdown(year: $year) { causeFamily count burnAreaHa }
      falseAlarmStats(year: $year) { district total falseAlarms rate }
      ignitionsHourly { hour count }
      responseTimeStats(year: $year) {
        count
        medianDispatchToArrivalSeconds
        medianArrivalToControlSeconds
      }
    }
  }
`

interface SeasonStatsResponse {
  stats: {
    activeFires: number
    today: number
    yesterday: number
    week: number
    burnAreaTotalHa: number | null
    totals: SeasonStats['header']['totals']
    current: SeasonStats['ignitionsCurrent']
    previous: SeasonStats['ignitionsPrevious']
    burnAreaCumulative: SeasonStats['burnAreaCumulative']
    causeBreakdown: SeasonStats['causeBreakdown']
    falseAlarmStats: SeasonStats['falseAlarmStats']
    ignitionsHourly: SeasonStats['ignitionsHourly']
    responseTimeStats: SeasonStats['responseTimeStats']
  }
}

export const fetchSeasonStats = createServerFn({ method: 'GET' })
  .validator((year: number) => year)
  .handler(async ({ data: year }): Promise<SeasonStats> => {
    const data = await graphql<SeasonStatsResponse>(SEASON_STATS_QUERY, {
      year,
      prevYear: year - 1,
    })
    const s = data.stats
    return {
      year,
      header: {
        activeFires: s.activeFires,
        today: s.today,
        yesterday: s.yesterday,
        week: s.week,
        burnAreaTotalHa: s.burnAreaTotalHa,
        totals: s.totals,
      },
      ignitionsCurrent: s.current,
      ignitionsPrevious: s.previous,
      burnAreaCumulative: s.burnAreaCumulative,
      causeBreakdown: s.causeBreakdown,
      falseAlarmStats: s.falseAlarmStats,
      ignitionsHourly: s.ignitionsHourly,
      responseTimeStats: s.responseTimeStats,
    }
  })

export const seasonStatsQuery = (year: number) =>
  queryOptions({
    queryKey: ['season-stats', year] as const,
    queryFn: () => fetchSeasonStats({ data: year }),
    staleTime: 5 * 60_000,
    refetchInterval: 5 * 60_000,
  })

// ── Concelho profile (WP3) ───────────────────────────────────────────────────

const CONCELHO_PROFILE_QUERY = /* GraphQL */ `
  query Concelho($dico: String!) {
    concelhoProfile(dico: $dico) {
      dico
      name
      district
      risk { date level label }
      activeIncidents { ${INCIDENT_LIST_FIELDS} }
      weatherWarnings {
        id
        areaCode
        awarenessType
        level
        levelPt
        startsAt
        endsAt
        text
      }
      yearIgnitions
      previousYearIgnitions
      yearBurnAreaHa
    }
  }
`

export const fetchConcelhoProfile = createServerFn({ method: 'GET' })
  .validator((dico: string) => dico)
  .handler(async ({ data: dico }) => {
    const data = await graphql<{ concelhoProfile: ConcelhoProfile | null }>(
      CONCELHO_PROFILE_QUERY,
      { dico },
    )
    return data.concelhoProfile
  })

export const concelhoProfileQuery = (dico: string) =>
  queryOptions({
    queryKey: ['concelho-profile', dico] as const,
    queryFn: () => fetchConcelhoProfile({ data: dico }),
    staleTime: 60_000,
    refetchInterval: 5 * 60_000,
  })

// ── Fire risk map (/risco) ───────────────────────────────────────────────────

/** Forecast horizon for the national fire-risk map. */
export type RiskDay = 'TODAY' | 'TOMORROW' | 'AFTER'

/** Normalised per-concelho feature properties served to the /risco map. */
export interface RiskFeatureProps {
  /** Zero-padded 4-char DICO code. */
  dico: string
  /** Title-cased concelho name. */
  name: string
  /** 1–5 fire-risk level, or 0 when the horizon has no value for the concelho. */
  level: number
}

export type RiskFeatureCollection = FeatureCollection<Geometry, RiskFeatureProps>

/** The national fire-risk payload for one horizon. */
export interface FireRiskCountry {
  /** Date the forecast applies to (YYYY-MM-DD), or null when none is stored. */
  forecastDate: string | null
  /** Per-concelho polygons with normalised risk levels, or null when no run. */
  geoJson: RiskFeatureCollection | null
}

const FIRE_RISK_COUNTRY_QUERY = /* GraphQL */ `
  query FireRiskCountry($day: RiskDay!) {
    fireRisk(day: $day) {
      forecastDate
      geoJson
    }
  }
`

/** Title-case a raw uppercase concelho name (pt-PT), e.g. ÁGUEDA → Águeda. */
function titleCaseConcelho(raw: string): string {
  return raw
    .toLocaleLowerCase('pt-PT')
    .replace(/(^|[\s\-])(\p{L})/gu, (_, sep, ch) => sep + ch.toLocaleUpperCase('pt-PT'))
}

/**
 * Flatten the stored horizon GeoJSON — whose per-concelho features carry
 * `properties.DICO`/`Concelho` and an optional `properties.data.rcm` (1–5) — into
 * the lean `{ dico, name, level }` shape the map colours by, dropping the raw
 * RCM object. Names are resolved from the canonical concelho set (the polygon
 * set is exactly those 278 mainland concelhos), falling back to a title-cased
 * copy of the embedded name.
 */
function normalizeRiskGeoJson(raw: string): RiskFeatureCollection {
  const parsed = JSON.parse(raw) as {
    features?: Array<{
      geometry: Geometry
      properties?: {
        DICO?: string
        Concelho?: string
        data?: { rcm?: number } | null
      }
    }>
  }
  const features = (parsed.features ?? []).map((f) => {
    const dico = f.properties?.DICO ?? ''
    const level =
      typeof f.properties?.data?.rcm === 'number' ? f.properties.data.rcm : 0
    const name =
      concelhoByDico(dico)?.name ??
      titleCaseConcelho(f.properties?.Concelho ?? '')
    return {
      type: 'Feature' as const,
      geometry: f.geometry,
      properties: { dico, name, level },
    }
  })
  return { type: 'FeatureCollection', features }
}

export const fetchFireRiskCountry = createServerFn({ method: 'GET' })
  .validator((day: RiskDay) => day)
  .handler(async ({ data: day }): Promise<FireRiskCountry> => {
    const data = await graphql<{
      fireRisk: { forecastDate: string | null; geoJson: string | null }
    }>(FIRE_RISK_COUNTRY_QUERY, { day })
    const { forecastDate, geoJson } = data.fireRisk
    return {
      forecastDate,
      geoJson: geoJson ? normalizeRiskGeoJson(geoJson) : null,
    }
  })

export const fireRiskCountryQuery = (day: RiskDay) =>
  queryOptions({
    queryKey: ['fire-risk-country', day] as const,
    queryFn: () => fetchFireRiskCountry({ data: day }),
    // Forecasts refresh a few times a day; a generous stale window is plenty.
    staleTime: 30 * 60_000,
    refetchInterval: 30 * 60_000,
  })

// ── Incidents table page (/ocorrencias) ──────────────────────────────────────

const INCIDENTS_PAGE_QUERY = /* GraphQL */ `
  query IncidentsPage($filter: IncidentFilter, $after: String) {
    incidents(filter: $filter, first: 50, after: $after) {
      totalCount
      pageInfo { hasNextPage endCursor }
      nodes { ${INCIDENT_LIST_FIELDS} }
    }
  }
`

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
 * Resolve the page's search params into the GraphQL `IncidentFilter`. Buckets
 * only constrain `statusCodes` when a strict subset is selected (all five = no
 * constraint); `kind`/`all` are never set so the default fire-only view stands.
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

interface IncidentsPage {
  incidents: {
    totalCount: number
    pageInfo: { hasNextPage: boolean; endCursor: string | null }
    nodes: IncidentListItem[]
  }
}

export const fetchIncidentsPage = createServerFn({ method: 'GET' })
  .validator((input: { filter: IncidentsFilter; after?: string | null }) => input)
  .handler(async ({ data }) => {
    return await graphql<IncidentsPage>(INCIDENTS_PAGE_QUERY, {
      filter: data.filter,
      after: data.after ?? null,
    })
  })

export const incidentsPageQuery = (filter: IncidentsFilter) =>
  infiniteQueryOptions({
    queryKey: ['incidents-page', filter] as const,
    queryFn: ({ pageParam }) =>
      fetchIncidentsPage({ data: { filter, after: pageParam } }),
    initialPageParam: null as string | null,
    getNextPageParam: (last) =>
      last.incidents.pageInfo.hasNextPage
        ? last.incidents.pageInfo.endCursor
        : undefined,
    // Not live-critical: refetching an infinite query refetches every loaded
    // page, so no refetchInterval here (unlike the map feeds).
    staleTime: 30_000,
  })

// ── Situation reports (/situacao) ────────────────────────────────────────────

const SITUATION_REPORTS_QUERY = /* GraphQL */ `
  query SituationReports($first: Int!) {
    situationReports(first: $first) {
      id
      at
      slot
      body
      activeFires
      totalMan
      totalTerrain
      totalAerial
      topIncidentIds
    }
  }
`

export const fetchSituationReports = createServerFn({ method: 'GET' })
  .validator((first: number) => first)
  .handler(async ({ data: first }) => {
    const data = await graphql<{ situationReports: SituationReport[] }>(
      SITUATION_REPORTS_QUERY,
      { first },
    )
    return data.situationReports
  })

export const situationReportsQuery = (first: number) =>
  queryOptions({
    queryKey: ['situation-reports', first] as const,
    queryFn: () => fetchSituationReports({ data: first }),
    staleTime: 5 * 60_000,
    refetchInterval: 5 * 60_000,
  })

// ── Weather warnings (/avisos) ───────────────────────────────────────────────
//
// Automatic-only: IPMA meteorological awareness warnings scraped by the Worker.
// There is no manual warning channel anymore.

const WEATHER_WARNINGS_QUERY = /* GraphQL */ `
  query WeatherWarnings {
    weatherWarnings {
      id
      areaCode
      awarenessType
      level
      levelPt
      startsAt
      endsAt
      text
    }
  }
`

export const fetchWeatherWarnings = createServerFn({ method: 'GET' }).handler(
  async () => {
    const data = await graphql<{ weatherWarnings: WeatherWarning[] }>(
      WEATHER_WARNINGS_QUERY,
    )
    return data.weatherWarnings
  },
)

export const weatherWarningsQuery = () =>
  queryOptions({
    queryKey: ['weather-warnings'] as const,
    queryFn: () => fetchWeatherWarnings(),
    staleTime: 60_000,
    refetchInterval: 5 * 60_000,
  })

// ── Web Push alerts (/alertas, WP4) ──────────────────────────────────────────
//
// All of these use the anonymous first-party X-API-Key path (via `graphql`), NOT
// the Clerk Bearer path in account-api.ts — an /alertas visitor is never signed
// in. The device id returned by `registerWebPushDevice` is a capability (a random
// GUID only the owning browser holds), so read/mutate calls that carry it need no
// further auth. Mutating calls forward the real browser IP (see
// `forwardedClientIpHeaders`) so the API's per-IP gates throttle per-visitor.

/** Anonymous create-subscription input: the shared shape plus the device to deliver to. */
export interface DeviceAlertSubscriptionInput extends AlertSubscriptionInput {
  deviceId: string
}

const WEB_PUSH_PUBLIC_KEY_QUERY = /* GraphQL */ `
  query WebPushPublicKey {
    webPushPublicKey
  }
`

// null ⇒ VAPID unconfigured ⇒ the feature is dark (the page shows a disabled card).
export const fetchWebPushPublicKey = createServerFn({ method: 'GET' }).handler(
  async () => {
    const data = await graphql<{ webPushPublicKey: string | null }>(
      WEB_PUSH_PUBLIC_KEY_QUERY,
    )
    return data.webPushPublicKey
  },
)

export const webPushPublicKeyQuery = () =>
  queryOptions({
    queryKey: ['web-push-public-key'] as const,
    queryFn: () => fetchWebPushPublicKey(),
    // The VAPID key is fixed for the lifetime of the server process (mirrors
    // accountsEnabledQuery): fetch once, never refetch.
    staleTime: Number.POSITIVE_INFINITY,
    gcTime: Number.POSITIVE_INFINITY,
  })

const REGISTER_WEB_PUSH_DEVICE_MUTATION = /* GraphQL */ `
  mutation RegisterWebPushDevice($input: RegisterWebPushDeviceInput!) {
    registerWebPushDevice(input: $input) { id }
  }
`

export const registerWebPushDevice = createServerFn({ method: 'POST' })
  .validator((input: { endpoint: string; p256dh: string; auth: string }) => input)
  .handler(async ({ data: input }): Promise<RegisteredDevice> => {
    const data = await graphql<{ registerWebPushDevice: RegisteredDevice }>(
      REGISTER_WEB_PUSH_DEVICE_MUTATION,
      { input },
      forwardedClientIpHeaders(),
    )
    return data.registerWebPushDevice
  })

const DELETE_WEB_PUSH_DEVICE_MUTATION = /* GraphQL */ `
  mutation DeleteWebPushDevice($endpoint: String!) {
    deleteWebPushDevice(endpoint: $endpoint)
  }
`

export const deleteWebPushDevice = createServerFn({ method: 'POST' })
  .validator((endpoint: string) => endpoint)
  .handler(async ({ data: endpoint }): Promise<boolean> => {
    const data = await graphql<{ deleteWebPushDevice: boolean }>(
      DELETE_WEB_PUSH_DEVICE_MUTATION,
      { endpoint },
      forwardedClientIpHeaders(),
    )
    return data.deleteWebPushDevice
  })

// Fields mirror account-api's ALERT_SUB_FIELDS so the result matches OwnedAlertSubscription.
const DEVICE_ALERT_SUB_FIELDS = /* GraphQL */ `
  id
  kind
  dico
  point { latitude longitude }
  radiusKm
  riskThreshold
  createdAt
  lastSeenAt
`

const DEVICE_SUBSCRIPTIONS_QUERY = /* GraphQL */ `
  query DeviceSubscriptions($deviceId: ID!) {
    deviceSubscriptions(deviceId: $deviceId) { ${DEVICE_ALERT_SUB_FIELDS} }
  }
`

export const fetchDeviceSubscriptions = createServerFn({ method: 'GET' })
  .validator((deviceId: string) => deviceId)
  .handler(async ({ data: deviceId }): Promise<OwnedAlertSubscription[]> => {
    const data = await graphql<{ deviceSubscriptions: OwnedAlertSubscription[] }>(
      DEVICE_SUBSCRIPTIONS_QUERY,
      { deviceId },
    )
    return data.deviceSubscriptions
  })

export const deviceSubscriptionsQuery = (deviceId: string) =>
  queryOptions({
    queryKey: ['device-subscriptions', deviceId] as const,
    queryFn: () => fetchDeviceSubscriptions({ data: deviceId }),
    staleTime: 30_000,
  })

const CREATE_DEVICE_ALERT_SUB_MUTATION = /* GraphQL */ `
  mutation CreateDeviceAlertSubscription($input: CreateAlertSubscriptionInput!) {
    createAlertSubscription(input: $input) { ${DEVICE_ALERT_SUB_FIELDS} }
  }
`

export const createDeviceAlertSubscription = createServerFn({ method: 'POST' })
  .validator((input: DeviceAlertSubscriptionInput) => input)
  .handler(async ({ data: input }): Promise<OwnedAlertSubscription> => {
    const data = await graphql<{ createAlertSubscription: OwnedAlertSubscription }>(
      CREATE_DEVICE_ALERT_SUB_MUTATION,
      { input },
      forwardedClientIpHeaders(),
    )
    return data.createAlertSubscription
  })

// Anonymous delete: the API's deleteAlertSubscription removes an unowned (device-
// bound, OwnerUserId null) subscription for an anonymous caller. This is the
// X-API-Key twin of account-api.ts's Clerk-path deleteAlertSubscription — that one
// requires a signed-in owner, so it can't serve the anonymous /alertas flow.
const DELETE_DEVICE_ALERT_SUB_MUTATION = /* GraphQL */ `
  mutation DeleteDeviceAlertSubscription($id: ID!) {
    deleteAlertSubscription(id: $id)
  }
`

export const deleteDeviceAlertSubscription = createServerFn({ method: 'POST' })
  .validator((id: string) => id)
  .handler(async ({ data: id }): Promise<boolean> => {
    const data = await graphql<{ deleteAlertSubscription: boolean }>(
      DELETE_DEVICE_ALERT_SUB_MUTATION,
      { id },
      forwardedClientIpHeaders(),
    )
    return data.deleteAlertSubscription
  })
