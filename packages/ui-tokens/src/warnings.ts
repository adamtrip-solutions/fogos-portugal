// IPMA weather-warning presentation tokens: level colors, severity ordering, the
// areaCode→district mapping, and the validity formatter. Ported verbatim from the
// web app's /avisos route (apps/web/src/routes/avisos.tsx). Mappings/labels only —
// no React / RN imports. The grouping/reshaping lives in @fogos/api-client.

// ── Level presentation ───────────────────────────────────────────────────────
// Colors match the concelho profile palette; the label comes from the API's
// `levelPt`.

const WARNING_LEVEL_COLOR: Record<string, string> = {
  yellow: '#F5B301',
  orange: '#FF6E02',
  red: '#B81E1F',
}

const WARNING_LEVEL_RANK: Record<string, number> = { red: 3, orange: 2, yellow: 1 }

/** Hex color for a warning level (yellow/orange/red); gray for anything unknown. */
export function warningLevelColor(level: string): string {
  return WARNING_LEVEL_COLOR[level.toLowerCase()] ?? '#BDBDBD'
}

/** Severity rank for sorting (red 3 > orange 2 > yellow 1); 0 for anything unknown. */
export function warningLevelRank(level: string): number {
  return WARNING_LEVEL_RANK[level.toLowerCase()] ?? 0
}

// ── IPMA area code → district name (mirrors the API's IpmaAreaCatalog) ────────

const AREA_TO_DISTRICT: Record<string, string> = {
  AVR: 'Aveiro',
  BJA: 'Beja',
  BGC: 'Bragança',
  BRG: 'Braga',
  CBR: 'Coimbra',
  CTB: 'Castelo Branco',
  EVR: 'Évora',
  FAR: 'Faro',
  GDA: 'Guarda',
  LRA: 'Leiria',
  LSB: 'Lisboa',
  PTG: 'Portalegre',
  PTO: 'Porto',
  STR: 'Santarém',
  STB: 'Setúbal',
  VCT: 'Viana do Castelo',
  VRL: 'Vila Real',
  VIS: 'Viseu',
  MCN: 'Madeira',
  MCS: 'Madeira',
  MMT: 'Madeira',
  PSA: 'Madeira',
  AOC: 'Açores',
  ACE: 'Açores',
  AOR: 'Açores',
}

/** District name for an IPMA area code; falls back to the raw code when unknown. */
export function districtForArea(areaCode: string): string {
  return AREA_TO_DISTRICT[areaCode.trim().toUpperCase()] ?? areaCode
}

// ── Validity formatting (pt-PT weekday + time) ───────────────────────────────
// Hermes-safe: weekday + hour + minute options, no timeZone (device-local, which
// is Lisbon for the target audience — matches the other mobile formatters).

const VALIDITY_FMT = new Intl.DateTimeFormat('pt-PT', {
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

/** "até sáb, 18:00" — the end matters most for warnings already in force. */
export function formatWarningValidity(endsAt: string): string {
  return `até ${VALIDITY_FMT.format(new Date(endsAt))}`
}
