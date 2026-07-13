// Typed operation helpers — thin wrappers binding a document to its result type.

import type { FogosClient } from './client'
import { concelhoByDico } from './concelhos'
import {
  ACTIVE_INCIDENTS_QUERY,
  CONCELHO_PROFILE_QUERY,
  FIRE_RISK_COUNTRY_QUERY,
  INCIDENTS_PAGE_QUERY,
  INCIDENT_DETAIL_QUERY,
  RECENT_INCIDENTS_QUERY,
  REGISTER_APP_DEVICE_MUTATION,
  SEASON_STATS_QUERY,
  SITUATION_REPORTS_QUERY,
  WEATHER_WARNINGS_QUERY,
} from './documents'
import { lisbonDateDaysAgo } from './time'
import type { IncidentsFilter } from './incidents-filter'
import type {
  AppDeviceCredential,
  ConcelhoProfile,
  DayArea,
  DayCount,
  CauseCount,
  DistrictFalseAlarms,
  FireRiskCountry,
  HourBucket,
  IncidentConnection,
  IncidentDetail,
  IncidentFilter,
  IncidentListItem,
  IncidentsPage,
  RegisterAppDeviceInput,
  ResourceTotals,
  ResponseTimeStats,
  RiskDay,
  RiskFeature,
  RiskFeatureCollection,
  RiskGeometry,
  SeasonStats,
  SituationReport,
  WeatherWarning,
} from './types'

/** The live feed of active/ongoing incidents (status codes 3–9) for the map. */
export async function fetchActiveIncidents(
  client: FogosClient,
  signal?: AbortSignal,
): Promise<IncidentListItem[]> {
  const data = await client.graphql<{ activeIncidents: IncidentListItem[] }>(
    ACTIVE_INCIDENTS_QUERY,
    undefined,
    signal,
  )
  return data.activeIncidents
}

/** How many days of change history the recent feed's `updatedAfter` reaches back. */
const RECENT_FEED_DAYS = 3
/** Max pages (100/page) pulled from the breadth feed. */
const RECENT_FEED_PAGES = 5
/** Max pages pulled from the Em Resolução/Vigilância (7/9) truncation-guard tail. */
const ONGOING_TAIL_PAGES = 2

/**
 * Pages through the `incidents(filter, …)` connection up to `maxPages` (100
 * nodes/page), stopping early when there is no next page. Mirrors the web app's
 * `fetchIncidentFeed`.
 */
async function fetchIncidentFeed(
  client: FogosClient,
  filter: IncidentFilter,
  maxPages: number,
  signal?: AbortSignal,
): Promise<IncidentListItem[]> {
  const nodes: IncidentListItem[] = []
  let cursor: string | null = null

  for (let page = 0; page < maxPages; page++) {
    const data: { incidents: IncidentConnection } = await client.graphql<{
      incidents: IncidentConnection
    }>(RECENT_INCIDENTS_QUERY, { filter, cursor }, signal)
    nodes.push(...data.incidents.nodes)
    if (!data.incidents.pageInfo.hasNextPage) break
    cursor = data.incidents.pageInfo.endCursor
  }

  return nodes
}

/**
 * The map's "recent + finished" feed, replicating the web app's
 * `fetchRecentIncidents` EXACTLY (apps/web/src/lib/fogos/api.ts):
 *
 *  1. A breadth feed — `updatedAfter` = Lisbon (now − 3 days), up to 5 pages.
 *     Change-based fetch keeps long-running Em Resolução/Vigilância fires (which
 *     the `activeIncidents` 3–6 feed never covers) in the payload, and lets
 *     closed fires drop 3 days after their last change.
 *  2. A truncation-guard tail — the same `updatedAfter` bound plus
 *     `statusCodes:[7,9]`, up to 2 pages. The connection sorts by `occurredAt`
 *     desc while the filter is `updatedAt`-based, so under heavy load the page
 *     cap would truncate the OLDEST-STARTED fires first — exactly the
 *     long-runners; this pins recently-touched 7/9 fires so they can't be lost.
 *     The tail keeps the SAME `updatedAfter` bound on purpose (nothing closes a
 *     7/9 fire ANEPC drops, so an unbounded tail would accrue stale dots).
 *
 * The two feeds merge by id. The FETCH stays `updatedAt`-based on purpose;
 * VISIBILITY is decided separately by the display-window predicate.
 */
export async function fetchRecentIncidents(
  client: FogosClient,
  signal?: AbortSignal,
): Promise<IncidentListItem[]> {
  const after = lisbonDateDaysAgo(RECENT_FEED_DAYS)
  const [changed, ongoingTail] = await Promise.all([
    fetchIncidentFeed(client, { updatedAfter: after }, RECENT_FEED_PAGES, signal),
    fetchIncidentFeed(
      client,
      { updatedAfter: after, statusCodes: [7, 9] },
      ONGOING_TAIL_PAGES,
      signal,
    ),
  ])

  const byId = new Map<string, IncidentListItem>()
  for (const inc of [...changed, ...ongoingTail]) byId.set(inc.id, inc)
  return [...byId.values()]
}

/**
 * Fetches one page of the incidents-table connection (`incidents(filter,
 * first:50, after)`). `after` is the cursor for the next page (null for the
 * first). Backs the Ocorrências infinite list; mirrors web's `fetchIncidentsPage`.
 */
export async function fetchIncidentsPage(
  client: FogosClient,
  filter: IncidentsFilter,
  after: string | null,
  signal?: AbortSignal,
): Promise<IncidentsPage> {
  const data = await client.graphql<{ incidents: IncidentsPage }>(
    INCIDENTS_PAGE_QUERY,
    { filter, after: after ?? null },
    signal,
  )
  return data.incidents
}

/**
 * Full detail for one incident (`incident(id)`), or null when the id is unknown.
 * Backs the incident bottom sheet; polled every 60 s while the sheet is open.
 */
export async function fetchIncident(
  client: FogosClient,
  id: string,
  signal?: AbortSignal,
): Promise<IncidentDetail | null> {
  const data = await client.graphql<{ incident: IncidentDetail | null }>(
    INCIDENT_DETAIL_QUERY,
    { id },
    signal,
  )
  return data.incident
}

/** Raw shape of the `Season` query response, before reshaping into SeasonStats. */
interface SeasonStatsResponse {
  stats: {
    activeFires: number
    today: number
    yesterday: number
    week: number
    burnAreaTotalHa: number | null
    totals: ResourceTotals | null
    current: DayCount[]
    previous: DayCount[]
    burnAreaCumulative: DayArea[]
    causeBreakdown: CauseCount[]
    falseAlarmStats: DistrictFalseAlarms[]
    ignitionsHourly: HourBucket[]
    responseTimeStats: ResponseTimeStats | null
  }
}

/**
 * Fetches the whole Estatísticas dashboard in one round-trip. Reshapes the raw
 * `Season` response into {@link SeasonStats}, mirroring the web app's
 * `fetchSeasonStats` (apps/web/src/lib/fogos/api.ts) exactly. `prevYear` is
 * always `year - 1` (the year-over-year baseline).
 */
export async function fetchSeasonStats(
  client: FogosClient,
  year: number,
  signal?: AbortSignal,
): Promise<SeasonStats> {
  const data = await client.graphql<SeasonStatsResponse>(
    SEASON_STATS_QUERY,
    { year, prevYear: year - 1 },
    signal,
  )
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
}

/**
 * The nationwide situation-report archive (`situationReports(first)`), newest
 * first. `first` caps how many reports come back. Mirrors web's
 * `fetchSituationReports`; backs the /situacao screen.
 */
export async function fetchSituationReports(
  client: FogosClient,
  first: number,
  signal?: AbortSignal,
): Promise<SituationReport[]> {
  const data = await client.graphql<{ situationReports: SituationReport[] }>(
    SITUATION_REPORTS_QUERY,
    { first },
    signal,
  )
  return data.situationReports
}

/**
 * In-force IPMA meteorological awareness warnings (`weatherWarnings`). Mirrors
 * web's `fetchWeatherWarnings`; backs the /avisos screen.
 */
export async function fetchWeatherWarnings(
  client: FogosClient,
  signal?: AbortSignal,
): Promise<WeatherWarning[]> {
  const data = await client.graphql<{ weatherWarnings: WeatherWarning[] }>(
    WEATHER_WARNINGS_QUERY,
    undefined,
    signal,
  )
  return data.weatherWarnings
}

/**
 * Registers this app install anonymously and returns its device credential.
 * Call through an anonymous client (no device header) to avoid a credential
 * loop.
 */
export async function registerAppDevice(
  client: FogosClient,
  input: RegisterAppDeviceInput,
  signal?: AbortSignal,
): Promise<AppDeviceCredential> {
  const data = await client.graphql<{ registerAppDevice: AppDeviceCredential }>(
    REGISTER_APP_DEVICE_MUTATION,
    { input },
    signal,
  )
  return data.registerAppDevice
}

/**
 * The concelho profile (`concelhoProfile(dico)`), or null when the DICO is
 * unknown. Backs the concelho screen; mirrors web's `fetchConcelhoProfile`.
 */
export async function fetchConcelhoProfile(
  client: FogosClient,
  dico: string,
  signal?: AbortSignal,
): Promise<ConcelhoProfile | null> {
  const data = await client.graphql<{ concelhoProfile: ConcelhoProfile | null }>(
    CONCELHO_PROFILE_QUERY,
    { dico },
    signal,
  )
  return data.concelhoProfile
}

/** Title-case a raw uppercase concelho name (pt-PT), e.g. ÁGUEDA → Águeda. */
function titleCaseConcelho(raw: string): string {
  return raw
    .toLocaleLowerCase('pt-PT')
    .replace(/(^|[\s-])(\p{L})/gu, (_, sep, ch) => sep + ch.toLocaleUpperCase('pt-PT'))
}

/**
 * Flatten the stored horizon GeoJSON — whose per-concelho features carry
 * `properties.DICO`/`Concelho` and an optional `properties.data.rcm` (1–5) — into
 * the lean `{ dico, name, level }` shape the map colours by, dropping the raw RCM
 * object. Names resolve from the canonical concelho set (the polygon set is
 * exactly those 278 mainland concelhos), falling back to a title-cased copy of
 * the embedded name. Ported verbatim from web's `normalizeRiskGeoJson`.
 */
function normalizeRiskGeoJson(raw: string): RiskFeatureCollection {
  const parsed = JSON.parse(raw) as {
    features?: Array<{
      geometry: RiskGeometry
      properties?: {
        DICO?: string
        Concelho?: string
        data?: { rcm?: number } | null
      }
    }>
  }
  const features: RiskFeature[] = (parsed.features ?? []).map((f) => {
    const dico = f.properties?.DICO ?? ''
    const level =
      typeof f.properties?.data?.rcm === 'number' ? f.properties.data.rcm : 0
    const name =
      concelhoByDico(dico)?.name ??
      titleCaseConcelho(f.properties?.Concelho ?? '')
    return {
      type: 'Feature',
      geometry: f.geometry,
      properties: { dico, name, level },
    }
  })
  return { type: 'FeatureCollection', features }
}

/**
 * The national fire-risk choropleth for one horizon (`fireRisk(day)`). Flattens
 * the raw `geoJson` string into normalised `{dico,name,level}` features. Mirrors
 * web's `fetchFireRiskCountry`; backs the /risco map.
 */
export async function fetchFireRiskCountry(
  client: FogosClient,
  day: RiskDay,
  signal?: AbortSignal,
): Promise<FireRiskCountry> {
  const data = await client.graphql<{
    fireRisk: { forecastDate: string | null; geoJson: string | null }
  }>(FIRE_RISK_COUNTRY_QUERY, { day }, signal)
  const { forecastDate, geoJson } = data.fireRisk
  return {
    forecastDate,
    geoJson: geoJson ? normalizeRiskGeoJson(geoJson) : null,
  }
}
