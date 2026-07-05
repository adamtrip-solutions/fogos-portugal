// Hand-written types mirroring the HotChocolate GraphQL schema.
// Field names are camelCase; `kind` serializes as a GraphQL enum name.

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

export interface IncidentResources {
  man: number
  terrain: number
  aerial: number
  aquatic: number
  heliFight: number
  heliCoord: number
  planeFight: number
}

export interface IncidentWeather {
  stationName: string
  distanceKm: number
  at: string
  temperature: number | null
  humidity: number | null
  windSpeedKmh: number | null
  windDirection: string | null
}

export interface StatusHistoryEntry {
  at: string
  code: number
  label: string
}

/** Per-incident resource time series; `-1` means unknown for any field. */
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
 * Derived escalation / rekindle / critical-conditions signals. Never null on
 * the API (absent → all-false defaults).
 */
export interface IncidentSignals {
  escalating: boolean
  escalationDetectedAt: string | null
  peakAssets: number | null
  rekindle: boolean
  rekindleOfId: string | null
  rekindleDetectedAt: string | null
  criticalConditions: boolean
  /** Machine keys — see CRITICAL_REASON_LABELS in format.ts. */
  criticalReasons: string[]
  conditionsEvaluatedAt: string | null
}

/** The subset of signals queried on list items (map badges + panel header). */
export interface IncidentSignalsLite {
  escalating: boolean
  rekindle: boolean
  criticalConditions: boolean
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

/** Metadata of a stored KML perimeter version (raw KML fetched via REST by id). */
export interface KmlVersionMeta {
  id: string
  vost: boolean
  capturedAt: string
  sizeBytes: number
}

/** A single NASA FIRMS thermal hotspot sample. */
export interface HotspotSample {
  position: Coordinates
  acquiredAt: string | null
  brightness: number | null
  confidence: string | null
}

/** VIIRS + MODIS hotspot samples for one incident. */
export interface Hotspots {
  incidentId: string
  viirs: HotspotSample[]
  modis: HotspotSample[]
  fetchedAt: string
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
  resources: Pick<IncidentResources, 'man' | 'terrain' | 'aerial' | 'aquatic'>
  /** Escalation / rekindle / critical-conditions flags for badges and the map pulse. */
  signals: IncidentSignalsLite
}

// ── Season statistics (WP3 stats fields) ─────────────────────────────────────

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

/** Everything the `/estatisticas` dashboard needs, fetched in one server round-trip. */
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

// ── Concelho profile (WP3) ───────────────────────────────────────────────────

/** One risk horizon in a concelho profile. */
export interface ConcelhoRiskDay {
  date: string
  /** 1..5 fire-risk level. */
  level: number
  label: string
}

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

/** Everything the `/concelho/$dico` page renders. */
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

// ── Alerts (WP4) ─────────────────────────────────────────────────────────────

export type AlertSubscriptionKind = 'CONCELHO' | 'POINT'

/** An anonymous alert subscription as returned by `createAlertSubscription`. */
export interface AlertSubscription {
  id: string
  kind: AlertSubscriptionKind
  dico: string | null
  point: Coordinates | null
  radiusKm: number | null
  riskThreshold: number | null
  createdAt: string
  lastSeenAt: string | null
}

/** A delivered alert event (`alertEvents`). */
export interface AlertEvent {
  id: string
  /** NEW_INCIDENT | ESCALATION | REKINDLE | RISK. */
  kind: string
  incidentId: string | null
  message: string
  createdAt: string
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
  kmlHistory: KmlVersionMeta[]
  hotspots: Hotspots | null
}
