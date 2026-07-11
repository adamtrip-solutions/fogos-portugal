// EFFIS Fire Weather Index (FWI) overlay.
//
// Unlike the IPMA weather layers (proxied tile-by-tile via /api/weather-tiles),
// the EFFIS raster is served directly from the Copernicus EMS WMS: CORS is `*`
// (verified live), so a MapLibre raster source can hit it with a
// `{bbox-epsg-3857}` tile template — no backend, no proxy. Layer `mf010.fwi` is
// MétéoFrance's 0.1° (~10 km) Fire Weather Index, with a `time=YYYY-MM-DD`
// forecast dimension (data present for today .. +3).

const EFFIS_ENDPOINT = 'https://maps.effis.emergency.copernicus.eu/effis'

/** European-Portuguese label shown in the layer control. */
export const FWI_LAYER_LABEL = 'Perigo de incêndio (FWI)'

/** Tooltip / helper copy (pt-PT). */
export const FWI_HELP_TEXT =
  'Índice meteorológico de perigo de incêndio (FWI) — Copernicus EFFIS, resolução ~10 km.'

/** Credit line shown in the control panel. */
export const FWI_CREDIT = 'Dados: Copernicus EFFIS'

/** MapLibre attribution added only while the layer is active. */
export const FWI_ATTRIBUTION = '© Copernicus EFFIS'

/** Day-selector labels (index 0..2 → today, +1, +2), computed in Europe/Lisbon. */
export const FIRE_DANGER_DAY_LABELS = ['Hoje', 'Amanhã', '+2 dias'] as const

/**
 * Official EFFIS FWI danger classes with the colours SAMPLED from the EFFIS
 * legend graphic for layer `mf010.fwi`:
 *   request=GetLegendGraphic&layer=mf010.fwi&format=image/png&version=1.3.0
 *   &service=WMS&sld_version=1.1.0
 * one hex per class, green → yellow → orange → red → very dark red. (The map
 * render collapses <11.2 into one green band and splits ≥50 into 50–70 / >70;
 * we present the standard 6-class danger scheme instead.)
 */
export const FIRE_DANGER_CLASSES = [
  { label: 'Muito baixo', range: '< 5,2', color: '#9CFFC0' },
  { label: 'Baixo', range: '5,2–11,2', color: '#CDE24E' },
  { label: 'Moderado', range: '11,2–21,3', color: '#E6AC00' },
  { label: 'Elevado', range: '21,3–38', color: '#D97010' },
  { label: 'Muito elevado', range: '38–50', color: '#AD060E' },
  { label: 'Extremo', range: '≥ 50', color: '#3A0015' },
] as const

/** Formats a Date as `YYYY-MM-DD` in the Europe/Lisbon calendar. */
function lisbonYmd(date: Date): string {
  // en-CA renders ISO-ordered `YYYY-MM-DD`.
  return new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Europe/Lisbon',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(date)
}

/**
 * The `time=` date for the FWI layer, `dayOffset` days after today in Lisbon.
 * Anchored at noon UTC before adding days so a DST transition can never shift
 * the calendar date.
 */
export function fireDangerDate(dayOffset: number): string {
  const [y, m, d] = lisbonYmd(new Date()).split('-').map(Number)
  const at = new Date(Date.UTC(y, m - 1, d, 12))
  at.setUTCDate(at.getUTCDate() + dayOffset)
  const yy = at.getUTCFullYear()
  const mm = String(at.getUTCMonth() + 1).padStart(2, '0')
  const dd = String(at.getUTCDate()).padStart(2, '0')
  return `${yy}-${mm}-${dd}`
}

/**
 * WMS 1.3.0 GetMap tile template for `mf010.fwi` at the given `YYYY-MM-DD`.
 * The `{bbox-epsg-3857}` token is left intact for MapLibre to substitute per
 * tile (256px, EPSG:3857, transparent PNG).
 */
export function effisFwiTiles(date: string): string {
  return (
    `${EFFIS_ENDPOINT}?service=WMS&version=1.3.0&request=GetMap` +
    `&layers=mf010.fwi&styles=&format=image/png&transparent=true` +
    `&crs=EPSG:3857&bbox={bbox-epsg-3857}&width=256&height=256&time=${date}`
  )
}
