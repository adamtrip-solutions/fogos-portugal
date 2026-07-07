import { createServerFn } from '@tanstack/react-start'
import { infiniteQueryOptions, queryOptions } from '@tanstack/react-query'

import { STATUS_BUCKETS, STATUS_BUCKET_CODES } from './format.ts'
import type { StatusBucket } from './format.ts'
import type {
  ConcelhoProfile,
  IncidentDetail,
  IncidentListItem,
  SeasonStats,
  SituationReport,
  Warning,
  WarningKind,
} from './types.ts'

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
      signals { escalating rekindle criticalConditions }
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
        signals { escalating rekindle criticalConditions }
      }
    }
  }
`

interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string }>
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
): Promise<T> {
  const endpoint = `${process.env.FOGOS_API_URL ?? 'http://localhost:5077'}/graphql`

  const res = await fetch(endpoint, {
    method: 'POST',
    headers: apiHeaders({ 'Content-Type': 'application/json' }),
    body: JSON.stringify({ query, variables }),
  })

  if (!res.ok) {
    throw new Error(`Fogos API responded with ${res.status}`)
  }

  const json = (await res.json()) as GraphQLResponse<T>

  if (json.errors && json.errors.length > 0) {
    throw new Error(json.errors.map((e) => e.message).join('; '))
  }

  if (!json.data) {
    throw new Error('Fogos API returned no data')
  }

  return json.data
}

// GraphQL calls run only on the server — the API has no CORS.
export const fetchActiveIncidents = createServerFn({ method: 'GET' }).handler(
  async () => {
    const data = await graphql<{ activeIncidents: IncidentListItem[] }>(
      ACTIVE_INCIDENTS_QUERY,
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
    const data: RecentIncidentsPage = await graphql<RecentIncidentsPage>(
      RECENT_INCIDENTS_QUERY,
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

// ── Broadcast warnings (/avisos) ─────────────────────────────────────────────

const WARNINGS_QUERY = /* GraphQL */ `
  query Warnings($kind: WarningKind) {
    warnings(kind: $kind) {
      id
      kind
      message
      url
      issuedBy
      createdAt
    }
  }
`

export const fetchWarnings = createServerFn({ method: 'GET' })
  .validator((kind: WarningKind | null) => kind)
  .handler(async ({ data: kind }) => {
    const data = await graphql<{ warnings: Warning[] }>(WARNINGS_QUERY, {
      kind: kind ?? null,
    })
    return data.warnings
  })

export const warningsQuery = (kind: WarningKind | null = null) =>
  queryOptions({
    queryKey: ['warnings', kind ?? 'all'] as const,
    queryFn: () => fetchWarnings({ data: kind }),
    staleTime: 60_000,
    refetchInterval: 2 * 60_000,
  })
