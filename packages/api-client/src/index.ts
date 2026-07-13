export { FogosApiError } from './errors'
export { createFogosClient } from './client'
export type {
  FogosClient,
  CreateFogosClientOptions,
} from './client'
export {
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
export {
  CONCELHOS,
  concelhoByDico,
  concelhoByName,
  foldText,
  searchConcelhos,
} from './concelhos'
export type { ConcelhoEntry } from './concelhos'
export { lisbonDateDaysAgo } from './time'
export {
  alignYoY,
  cumulativeSum,
  dayOfYear,
  latestBurnArea,
  yoyRatio,
} from './stats'
export type { YoYPoint } from './stats'
export { situationDeltas } from './situation'
export type {
  SituationDelta,
  SituationDeltaTone,
  SituationMetric,
} from './situation'
export { groupWarningsByDistrict } from './warnings'
export type { WarningDistrictGroup } from './warnings'
export {
  buildIncidentsFilter,
  INCIDENT_DISTRICTS,
} from './incidents-filter'
export type { IncidentsWindow, IncidentsFilter } from './incidents-filter'
export {
  fetchActiveIncidents,
  fetchConcelhoProfile,
  fetchFireRiskCountry,
  fetchIncident,
  fetchIncidentsPage,
  fetchRecentIncidents,
  fetchSeasonStats,
  fetchSituationReports,
  fetchWeatherWarnings,
  registerAppDevice,
} from './operations'
export type {
  AppDeviceCredential,
  AppPlatform,
  BurnArea,
  CauseCount,
  ConcelhoProfile,
  ConcelhoRiskDay,
  Coordinates,
  DayArea,
  DayCount,
  DistrictFalseAlarms,
  FireRiskCountry,
  Hotspots,
  HotspotSample,
  HourBucket,
  IncidentAircraft,
  IncidentConnection,
  IncidentDetail,
  IncidentFilter,
  IncidentIcnf,
  IncidentKind,
  IncidentListItem,
  IncidentPhoto,
  IncidentResources,
  IncidentSignals,
  IncidentSignalsLite,
  IncidentsPage,
  IncidentStatus,
  IncidentWeather,
  KmlVersionMeta,
  PageInfo,
  RegisterAppDeviceInput,
  ResourceSnapshot,
  ResourceTotals,
  ResponseTimes,
  ResponseTimeStats,
  RiskDay,
  RiskFeature,
  RiskFeatureCollection,
  RiskFeatureProps,
  RiskGeometry,
  SeasonHeaderStats,
  SeasonStats,
  SituationReport,
  StatusHistoryEntry,
  WeatherWarning,
} from './types'
