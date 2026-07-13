// GraphQL operation documents shared by every Fogos client. Plain strings so the
// package stays free of a codegen/build step and any GraphQL runtime dependency.

/**
 * The shared IncidentListItem selection — the exact node field set every list
 * feed (active map, recent feed, incidents-table page, concelho profile) selects.
 * Interpolated into each list query below so the four stay byte-identical in
 * fields; mirrors web's `INCIDENT_LIST_FIELDS` convention.
 *
 * The `demobilizedSince` signal (web selects it) is intentionally omitted here —
 * it is a not-yet-deployed backend field. The marker comment below is the single
 * place to re-enable it once the backend deploys it (zombie-fire display window).
 */
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
  signals {
    escalating
    rekindle
    criticalConditions
    # enable after backend deploys demobilizedSince (zombie-fire display window)
  }
`

/** Same field set the web map's list uses (activeIncidents → IncidentListItem). */
export const ACTIVE_INCIDENTS_QUERY = /* GraphQL */ `
  query Active {
    activeIncidents {${INCIDENT_LIST_FIELDS}}
  }
`

/**
 * Paginated incidents feed (`incidents(filter, first, after)`). Same node field
 * set as {@link ACTIVE_INCIDENTS_QUERY} so both feeds merge into one shape. The
 * web map drives two variants of this query: `updatedAfter` (breadth) and an
 * `updatedAfter` + `statusCodes:[7,9]` tail — see `fetchRecentIncidents`.
 */
export const RECENT_INCIDENTS_QUERY = /* GraphQL */ `
  query Recent($filter: IncidentFilter, $cursor: String) {
    incidents(filter: $filter, first: 100, after: $cursor) {
      pageInfo { hasNextPage endCursor }
      nodes {${INCIDENT_LIST_FIELDS}}
    }
  }
`

/**
 * The incidents-table page (`incidents(filter, first:50, after)`). Same node
 * field set web's IncidentsPage query uses (its INCIDENT_LIST_FIELDS subset — no
 * `demobilizedSince`, same as {@link RECENT_INCIDENTS_QUERY}), plus `totalCount`
 * for the "N de M ocorrências" footer. Drives the mobile Ocorrências infinite
 * list; NOT polled (a search surface, not the live map).
 */
export const INCIDENTS_PAGE_QUERY = /* GraphQL */ `
  query IncidentsPage($filter: IncidentFilter, $after: String) {
    incidents(filter: $filter, first: 50, after: $after) {
      totalCount
      pageInfo { hasNextPage endCursor }
      nodes {${INCIDENT_LIST_FIELDS}}
    }
  }
`

/**
 * Full incident detail (`incident(id)` → IncidentDetail). Ported verbatim from
 * the web app's `Detail` query (apps/web/src/lib/fogos/api.ts). Same field set:
 * full resources incl. the heli/coord/plane split, nearest-station weather,
 * status history, the resource-history series, ICNF cause/burn area, moderated
 * photos, the full signals block, response times, associated aircraft, and the
 * KML-perimeter + FIRMS-hotspot data (the last two are consumed by phase 3 — they
 * are cheap so they ride along in the document now, with no UI yet).
 *
 * `signals.demobilizedSince` is intentionally omitted (same marked-comment
 * convention as {@link RECENT_INCIDENTS_QUERY}): it is a not-yet-deployed backend
 * field, and the detail panel has no use for it.
 */
export const INCIDENT_DETAIL_QUERY = /* GraphQL */ `
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
        # demobilizedSince intentionally omitted — not-yet-deployed field, unused here
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

/**
 * The Estatísticas dashboard's single round-trip (`Season($year,$prevYear)`).
 * Ported verbatim from the web app's `SEASON_STATS_QUERY`
 * (apps/web/src/lib/fogos/api.ts). Calculated cost 13 — no budget concern.
 * `current`/`previous` alias `ignitionsByDay` for the year-over-year overlay.
 */
export const SEASON_STATS_QUERY = /* GraphQL */ `
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

/**
 * The nationwide situation-report archive (`situationReports(first)`). Ported
 * verbatim from the web app's `SITUATION_REPORTS_QUERY`
 * (apps/web/src/lib/fogos/api.ts). Newest-first; `first` caps how many reports
 * come back (the /situacao screen asks for 14 — the hero, the previous report
 * for deltas, and a two-week archive).
 */
export const SITUATION_REPORTS_QUERY = /* GraphQL */ `
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

/**
 * In-force IPMA meteorological awareness warnings (`weatherWarnings`). Ported
 * verbatim from the web app's `WEATHER_WARNINGS_QUERY`
 * (apps/web/src/lib/fogos/api.ts). Automatic-only — there is no manual warning
 * channel. Backs the /avisos screen.
 */
export const WEATHER_WARNINGS_QUERY = /* GraphQL */ `
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

/**
 * The concelho profile (`concelhoProfile(dico)`). Ported verbatim from the web
 * app's `CONCELHO_PROFILE_QUERY` (apps/web/src/lib/fogos/api.ts). Its
 * `activeIncidents` selection matches the shared IncidentListItem field set
 * (identical to {@link ACTIVE_INCIDENTS_QUERY}). Backs the concelho screen.
 */
export const CONCELHO_PROFILE_QUERY = /* GraphQL */ `
  query Concelho($dico: String!) {
    concelhoProfile(dico: $dico) {
      dico
      name
      district
      risk { date level label }
      activeIncidents {${INCIDENT_LIST_FIELDS}}
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

/**
 * The national fire-risk choropleth for one horizon (`fireRisk(day)`). Ported
 * verbatim from the web app's `FIRE_RISK_COUNTRY_QUERY`. `geoJson` comes back as
 * a raw JSON string that `fetchFireRiskCountry` flattens to `{dico,name,level}`
 * features. Backs the /risco map.
 */
export const FIRE_RISK_COUNTRY_QUERY = /* GraphQL */ `
  query FireRiskCountry($day: RiskDay!) {
    fireRisk(day: $day) {
      forecastDate
      geoJson
    }
  }
`

/**
 * Registers a mobile app install on first launch and mints its device-bound
 * credential (`fdv1.{deviceId}.{deviceSecret}`). Called anonymously; the caller
 * is IP-gated backend-side.
 */
export const REGISTER_APP_DEVICE_MUTATION = /* GraphQL */ `
  mutation RegisterAppDevice($input: RegisterAppDeviceInput!) {
    registerAppDevice(input: $input) {
      deviceId
      deviceSecret
    }
  }
`
