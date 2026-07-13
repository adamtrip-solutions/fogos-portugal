// Mobile Fogos client: builds the shared @fogos/api-client against the public
// endpoint, wires the device-key auth header, and adds the DEVICE_UNAUTHENTICATED
// re-register/retry recovery. All GraphQL types, documents, error mapping, and
// operation helpers live in @fogos/api-client — this file only supplies the
// native transport concerns (endpoint + credentials).

import {
  createFogosClient,
  fetchActiveIncidents as fetchActiveIncidentsOp,
  fetchConcelhoProfile as fetchConcelhoProfileOp,
  fetchFireRiskCountry as fetchFireRiskCountryOp,
  fetchIncident as fetchIncidentOp,
  fetchIncidentsPage as fetchIncidentsPageOp,
  fetchRecentIncidents as fetchRecentIncidentsOp,
  fetchSeasonStats as fetchSeasonStatsOp,
  fetchSituationReports as fetchSituationReportsOp,
  fetchWeatherWarnings as fetchWeatherWarningsOp,
  type ConcelhoProfile,
  type FireRiskCountry,
  type FogosClient,
  type IncidentDetail,
  type IncidentListItem,
  type IncidentsFilter,
  type IncidentsPage,
  type RiskDay,
  type SeasonStats,
  type SituationReport,
  type WeatherWarning,
} from '@fogos/api-client'

import { FOGOS_ENDPOINT } from './config'
import { deviceAuthHeaders, isDeviceUnauthenticated, reRegisterDevice } from './device'

export { FogosApiError } from '@fogos/api-client'
export type { IncidentDetail, IncidentListItem } from '@fogos/api-client'

const baseClient = createFogosClient({
  endpoint: FOGOS_ENDPOINT,
  getHeaders: deviceAuthHeaders,
})

/**
 * Device-key aware client. On a DEVICE_UNAUTHENTICATED 401 (present-but-invalid
 * credential) it wipes + re-registers once, then retries the request a single
 * time; anything else propagates. Registration failures fall through to an
 * anonymous request.
 */
export const fogosClient: FogosClient = {
  async graphql<T>(
    query: string,
    variables?: Record<string, unknown>,
    signal?: AbortSignal,
  ): Promise<T> {
    try {
      return await baseClient.graphql<T>(query, variables, signal)
    } catch (error) {
      if (isDeviceUnauthenticated(error) && (await reRegisterDevice())) {
        return baseClient.graphql<T>(query, variables, signal)
      }
      throw error
    }
  },
}

/** The live feed of active/ongoing incidents (status codes 3–9) for the map. */
export function fetchActiveIncidents(
  signal?: AbortSignal,
): Promise<IncidentListItem[]> {
  return fetchActiveIncidentsOp(fogosClient, signal)
}

/**
 * The "recent + finished" feed: `updatedAfter` breadth (5 pages) merged with an
 * Em Resolução/Vigilância (7/9) truncation-guard tail (2 pages). Mirrors the web
 * map's second feed; merged with the active feed by the map pipeline.
 */
export function fetchRecentIncidents(
  signal?: AbortSignal,
): Promise<IncidentListItem[]> {
  return fetchRecentIncidentsOp(fogosClient, signal)
}

/** Full detail for one incident (`incident(id)`); null when the id is unknown. */
export function fetchIncident(
  id: string,
  signal?: AbortSignal,
): Promise<IncidentDetail | null> {
  return fetchIncidentOp(fogosClient, id, signal)
}

/**
 * One page of the Ocorrências list (`incidents(filter, first:50, after)`).
 * `after` is the cursor for the next page (null for the first). Not polled — a
 * search surface, unlike the live map feeds.
 */
export function fetchIncidentsPage(
  filter: IncidentsFilter,
  after: string | null,
  signal?: AbortSignal,
): Promise<IncidentsPage> {
  return fetchIncidentsPageOp(fogosClient, filter, after, signal)
}

/**
 * The whole Estatísticas dashboard in one round-trip (`Season($year,$prevYear)`).
 * Not polled here — the screen sets a 5 min foreground `refetchInterval`.
 */
export function fetchSeasonStats(
  year: number,
  signal?: AbortSignal,
): Promise<SeasonStats> {
  return fetchSeasonStatsOp(fogosClient, year, signal)
}

/**
 * The nationwide situation-report archive (`situationReports(first)`), newest
 * first. Backs the Situação screen (5 min foreground `refetchInterval`).
 */
export function fetchSituationReports(
  first: number,
  signal?: AbortSignal,
): Promise<SituationReport[]> {
  return fetchSituationReportsOp(fogosClient, first, signal)
}

/**
 * In-force IPMA meteorological awareness warnings (`weatherWarnings`). Backs the
 * Avisos screen (5 min foreground `refetchInterval`).
 */
export function fetchWeatherWarnings(
  signal?: AbortSignal,
): Promise<WeatherWarning[]> {
  return fetchWeatherWarningsOp(fogosClient, signal)
}

/**
 * The concelho profile (`concelhoProfile(dico)`); null when the DICO is unknown.
 * Backs the Concelho screen (5 min foreground `refetchInterval`).
 */
export function fetchConcelhoProfile(
  dico: string,
  signal?: AbortSignal,
): Promise<ConcelhoProfile | null> {
  return fetchConcelhoProfileOp(fogosClient, dico, signal)
}

/**
 * The national fire-risk choropleth for one horizon (`fireRisk(day)`), with the
 * `geoJson` flattened to `{dico,name,level}` features. Backs the Risco screen
 * (30 min foreground `refetchInterval`).
 */
export function fetchFireRiskCountry(
  day: RiskDay,
  signal?: AbortSignal,
): Promise<FireRiskCountry> {
  return fetchFireRiskCountryOp(fogosClient, day, signal)
}
