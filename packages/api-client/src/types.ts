// Hand-written types mirroring the HotChocolate GraphQL schema. Field names are
// camelCase; `kind` serializes as a GraphQL enum name.
//
// Shared source of truth for every Fogos client (mobile today, web server fns
// later). No React / RN / DOM imports — pure data shapes.

export type IncidentKind =
  | 'FIRE'
  | 'URBAN_FIRE'
  | 'TRANSPORT_FIRE'
  | 'OTHER_FIRE'
  | 'FMA'
  | 'OTHER'
  | (string & {})

export interface Coordinates {
  latitude: number
  longitude: number
}

export interface IncidentStatus {
  /** Status codes 3-6 are considered active. */
  code: number
  label: string
  /** Hex without a leading `#`, e.g. "B81E1F". */
  color: string
}

/** Escalation / rekindle / critical-conditions flags queried on list items. */
export interface IncidentSignalsLite {
  escalating: boolean
  rekindle: boolean
  criticalConditions: boolean
  /**
   * When ANEPC first reported the fire fully demobilized (all resources stood
   * down), or null. NULLABLE + OPTIONAL on purpose: this field is a parallel
   * backend work stream that is NOT deployed yet, so it is deliberately absent
   * from the GraphQL query documents for now (see the marked comment in
   * `RECENT_INCIDENTS_QUERY`). The display-window predicate treats
   * missing/undefined as null. Wire the document field once the backend ships.
   */
  demobilizedSince?: string | null
}

/** Shape returned by the `activeIncidents` list query. */
export interface IncidentListItem {
  id: string
  location: string
  district: string | null
  concelho: string | null
  freguesia: string | null
  coordinates: Coordinates | null
  status: IncidentStatus
  kind: IncidentKind
  natureza: string | null
  important: boolean
  occurredAt: string
  updatedAt: string
  /** When the status last changed; null when no transition was ever recorded. */
  statusChangedAt: string | null
  resources: {
    man: number
    terrain: number
    aerial: number
    aquatic: number
  }
  /** Flags for badges and the map pulse/halo. */
  signals: IncidentSignalsLite
}

/**
 * Subset of the GraphQL `IncidentFilter` input the incident feeds drive. All
 * fields optional — the map feeds set `updatedAfter` (and a `statusCodes` tail);
 * the incidents table (later phase) sets `after`/`district`.
 */
export interface IncidentFilter {
  /** Records whose `updatedAt` is on/after this Lisbon calendar day (YYYY-MM-DD). */
  updatedAfter?: string
  /** Records whose `occurredAt` is on/after this Lisbon calendar day (YYYY-MM-DD). */
  after?: string
  /** Restrict to these raw status codes (e.g. `[7, 9]` for Em Resolução/Vigilância). */
  statusCodes?: number[]
  /** District name filter. */
  district?: string
}

/** Relay-style page metadata on a connection. */
export interface PageInfo {
  hasNextPage: boolean
  endCursor: string | null
}

/** A single page of the `incidents(filter, first, after)` connection. */
export interface IncidentConnection {
  pageInfo: PageInfo
  nodes: IncidentListItem[]
}

/**
 * One page of the incidents-table connection — same as {@link IncidentConnection}
 * but with the `totalCount` the /ocorrencias footer needs. Backs
 * {@link IncidentsFilter}-driven paging.
 */
export interface IncidentsPage {
  totalCount: number
  pageInfo: PageInfo
  nodes: IncidentListItem[]
}

// ── Incident detail (`incident(id)` → IncidentDetail) ────────────────────────
// Mirrors the web app's detail shapes (apps/web/src/lib/fogos/types.ts) 1:1.

/** Full per-incident resource counts; `-1` means undisclosed for any field. */
export interface IncidentResources {
  man: number
  terrain: number
  aerial: number
  aquatic: number
  heliFight: number
  heliCoord: number
  planeFight: number
}

/** Nearest weather-station reading. Any individual sensor can be null. */
export interface IncidentWeather {
  stationName: string
  distanceKm: number
  at: string
  temperature: number | null
  humidity: number | null
  windSpeedKmh: number | null
  windDirection: string | null
}

/** One observed status transition (newest-first when rendered). */
export interface StatusHistoryEntry {
  at: string
  code: number
  label: string
}

/** Per-incident resource time series point; `-1` means unknown for any field. */
export interface ResourceSnapshot {
  at: string
  man: number
  terrain: number
  aerial: number
}

export interface BurnArea {
  total: number | null
}

export interface IncidentIcnf {
  causeType: string | null
  cause: string | null
  burnArea: BurnArea | null
  updatedAt: string | null
}

export interface IncidentPhoto {
  id: string
  publicUrl: string
  width: number | null
  height: number | null
  takenAt: string | null
  /** EXIF GPS captured at upload; present for most citizen photos. */
  gps: Coordinates | null
}

/**
 * Full derived escalation / rekindle / critical-conditions signals (detail
 * query). Never null on the API (absent → all-false defaults).
 */
export interface IncidentSignals {
  escalating: boolean
  escalationDetectedAt: string | null
  peakAssets: number | null
  rekindle: boolean
  rekindleOfId: string | null
  rekindleDetectedAt: string | null
  criticalConditions: boolean
  /** Machine keys — see CRITICAL_REASON_LABELS in @fogos/ui-tokens. */
  criticalReasons: string[]
  conditionsEvaluatedAt: string | null
}

/** Operational response durations derived from the status log; null when empty. */
export interface ResponseTimes {
  dispatchToArrivalSeconds: number | null
  arrivalToControlSeconds: number | null
  controlToConclusionSeconds: number | null
  totalSeconds: number | null
}

/** An aircraft associated with the fire (link joined to the tracked fleet). */
export interface IncidentAircraft {
  icao: string
  registration: string | null
  name: string | null
  kind: string | null
  active: boolean
  firstSeenAt: string
  lastSeenAt: string
  samples: number
}

/** Metadata of a stored KML perimeter version (phase 3; raw KML fetched via REST). */
export interface KmlVersionMeta {
  id: string
  vost: boolean
  capturedAt: string
  sizeBytes: number
}

/** A single NASA FIRMS thermal hotspot sample (phase 3). */
export interface HotspotSample {
  position: Coordinates
  acquiredAt: string | null
  brightness: number | null
  confidence: string | null
}

/** VIIRS + MODIS hotspot samples for one incident (phase 3). */
export interface Hotspots {
  viirs: HotspotSample[]
  modis: HotspotSample[]
}

/** Shape returned by the `incident(id)` detail query. */
export interface IncidentDetail {
  id: string
  location: string
  detailLocation: string | null
  district: string | null
  concelho: string | null
  freguesia: string | null
  extra: string | null
  coordinates: Coordinates | null
  status: IncidentStatus
  kind: IncidentKind
  natureza: string | null
  important: boolean
  active: boolean
  occurredAt: string
  updatedAt: string
  resources: IncidentResources
  weather: IncidentWeather | null
  statusHistory: StatusHistoryEntry[]
  history: ResourceSnapshot[]
  icnf: IncidentIcnf | null
  photos: IncidentPhoto[]
  signals: IncidentSignals
  responseTimes: ResponseTimes | null
  aircraft: IncidentAircraft[]
  /** KML perimeter versions (phase 3 UI). */
  kmlHistory: KmlVersionMeta[]
  /** FIRMS hotspots (phase 3 UI). */
  hotspots: Hotspots | null
}

// ── Season statistics (WP3 stats fields) ─────────────────────────────────────
// Ported verbatim from apps/web/src/lib/fogos/types.ts. Backs the Estatísticas
// dashboard's single `Season($year,$prevYear)` query.

/** One calendar day and its fire-ignition count (`ignitionsByDay`). */
export interface DayCount {
  /** ISO `YYYY-MM-DD` (Lisbon calendar day). */
  date: string
  count: number
}

/** One calendar day and the cumulative accounted burn area in ha (`burnAreaCumulative`). */
export interface DayArea {
  date: string
  totalHa: number
}

/** Fire count + burn area for one ICNF cause family (`causeBreakdown`). */
export interface CauseCount {
  causeFamily: string
  count: number
  burnAreaHa: number
}

/** Per-district false-alarm counters + rate (`falseAlarmStats`). */
export interface DistrictFalseAlarms {
  district: string
  total: number
  falseAlarms: number
  /** falseAlarms / total, 0..1. */
  rate: number
}

/** Median first-transition response times for a season (`responseTimeStats`). */
export interface ResponseTimeStats {
  count: number
  medianDispatchToArrivalSeconds: number | null
  medianArrivalToControlSeconds: number | null
}

/** One hour of the ignition histogram (`ignitionsHourly`). */
export interface HourBucket {
  hour: number
  count: number
}

/** Latest nationwide resource totals (`stats.totals`). */
export interface ResourceTotals {
  man: number
  terrain: number
  aerial: number
  total: number
  at: string
}

/** The bundle of scalar season counters shown in the dashboard header tiles. */
export interface SeasonHeaderStats {
  activeFires: number
  today: number
  yesterday: number
  week: number
  burnAreaTotalHa: number | null
  totals: ResourceTotals | null
}

/** Everything the Estatísticas dashboard needs, fetched in one round-trip. */
export interface SeasonStats {
  year: number
  header: SeasonHeaderStats
  ignitionsCurrent: DayCount[]
  ignitionsPrevious: DayCount[]
  burnAreaCumulative: DayArea[]
  causeBreakdown: CauseCount[]
  falseAlarmStats: DistrictFalseAlarms[]
  ignitionsHourly: HourBucket[]
  responseTimeStats: ResponseTimeStats | null
}

/** The mobile app platform sent to `registerAppDevice` (GraphQL enum `AppPlatform`). */
export type AppPlatform = 'IOS' | 'ANDROID'

/** Variables for the `registerAppDevice` mutation. */
export interface RegisterAppDeviceInput {
  platform: AppPlatform
  model?: string | null
  appVersion?: string | null
}

/** Result of `registerAppDevice` — the device-bound credential minted on first launch. */
export interface AppDeviceCredential {
  deviceId: string
  deviceSecret: string
}

// ── Situation reports (twice-daily nationwide) ───────────────────────────────
// Ported verbatim from apps/web/src/lib/fogos/types.ts. Backs the /situacao screen.

/**
 * One twice-daily nationwide situation report. `slot` is `morning` (09:00
 * Lisbon) or `evening` (20:00 Lisbon); `body` is plain-text European Portuguese
 * with `\r\n` line breaks — preserve them when rendering.
 */
export interface SituationReport {
  id: string
  at: string
  slot: string
  body: string
  activeFires: number
  totalMan: number
  totalTerrain: number
  totalAerial: number
  /** Top active fires by mobilized assets (incident ids), for deep-linking. */
  topIncidentIds: string[]
}

// ── Weather warnings (IPMA awareness warnings) ───────────────────────────────
// Ported verbatim from apps/web/src/lib/fogos/types.ts. Backs the /avisos screen.

/** An in-force IPMA awareness warning mapped to the concelho's district. */
export interface WeatherWarning {
  id: string
  areaCode: string
  awarenessType: string
  /** yellow / orange / red. */
  level: string
  /** PT level label (Amarelo / Laranja / Vermelho). */
  levelPt: string
  startsAt: string
  endsAt: string
  text: string | null
}

// ── Concelho profile (/concelho/[dico]) ──────────────────────────────────────

/** One risk horizon in a concelho profile. */
export interface ConcelhoRiskDay {
  date: string
  /** 1..5 fire-risk level. */
  level: number
  label: string
}

/** Everything the concelho profile screen renders. */
export interface ConcelhoProfile {
  dico: string
  name: string
  district: string
  risk: ConcelhoRiskDay[]
  activeIncidents: IncidentListItem[]
  weatherWarnings: WeatherWarning[]
  yearIgnitions: number
  previousYearIgnitions: number
  yearBurnAreaHa: number
}

// ── National fire-risk map (/risco) ──────────────────────────────────────────

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

/**
 * The concelho polygon geometry. Concelho boundaries are always Polygon or
 * MultiPolygon; typed concretely (rather than pulling in the `geojson` package,
 * which this dependency-free client deliberately avoids) so callers can read
 * coordinates for a centroid. Structurally a GeoJSON geometry.
 */
export type RiskGeometry =
  | { type: 'Polygon'; coordinates: number[][][] }
  | { type: 'MultiPolygon'; coordinates: number[][][][] }

/** One normalised per-concelho polygon feature. */
export interface RiskFeature {
  type: 'Feature'
  geometry: RiskGeometry
  properties: RiskFeatureProps
}

/** The national fire-risk GeoJSON for one horizon. */
export interface RiskFeatureCollection {
  type: 'FeatureCollection'
  features: RiskFeature[]
}

/** The national fire-risk payload for one horizon. */
export interface FireRiskCountry {
  /** Date the forecast applies to (YYYY-MM-DD), or null when none is stored. */
  forecastDate: string | null
  /** Per-concelho polygons with normalised risk levels, or null when no run. */
  geoJson: RiskFeatureCollection | null
}
