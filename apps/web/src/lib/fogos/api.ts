import { createServerFn } from '@tanstack/react-start'
import { queryOptions } from '@tanstack/react-query'

import type {
  ConcelhoProfile,
  IncidentDetail,
  IncidentListItem,
  SeasonStats,
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
  query Recent($after: Date!, $cursor: String) {
    incidents(filter: { after: $after }, first: 100, after: $cursor) {
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

async function graphql<T>(
  query: string,
  variables?: Record<string, unknown>,
): Promise<T> {
  const endpoint = `${process.env.FOGOS_API_URL ?? 'http://localhost:5077'}/graphql`

  const res = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
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

// Recent incidents: fires that STARTED on/after (now − 3 days)'s Lisbon
// calendar day. Wider than the display window on purpose — multi-day fires
// still in resolution/vigilância only surface through this query (the
// activeIncidents feed covers codes 3–6 only); the view filters client-side.
// Paginates up to 5 pages (500 incidents), newest first.
export const fetchRecentIncidents = createServerFn({ method: 'GET' }).handler(
  async () => {
    const after = lisbonDateFmt.format(
      new Date(Date.now() - 3 * 24 * 60 * 60 * 1000),
    )

    const nodes: IncidentListItem[] = []
    let cursor: string | null = null

    for (let page = 0; page < 5; page++) {
      const data: RecentIncidentsPage = await graphql<RecentIncidentsPage>(
        RECENT_INCIDENTS_QUERY,
        { after, cursor },
      )
      nodes.push(...data.incidents.nodes)
      if (!data.incidents.pageInfo.hasNextPage) break
      cursor = data.incidents.pageInfo.endCursor
    }

    return nodes
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
