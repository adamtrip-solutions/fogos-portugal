// Weather overlay catalog.
//
// Two kinds of overlay live here. IPMA WMS products (`kind: 'wms'`) are proxied
// tile-by-tile; verified live against `https://mf2.ipma.pt/services/?` (WMS
// 1.3.0). The fire risk product needs no time parameters; the AROME products
// require both `time` and `reference_time` bound to a published model run.
// Region suffixes (`.continent`, `.madeira`, `.azores`) select the geographic
// domain. RainViewer radar (`kind: 'radar'`) is not WMS-based — it is fetched
// and animated client-side (see `radar.ts`), so it carries no WMS metadata.

export type WeatherLayerKey =
  | 'risk'
  | 'radar'
  | 'temperature'
  | 'wind'
  | 'gusts'
  | 'humidity'

export type WeatherRegion = 'continent' | 'madeira' | 'azores'

/** IPMA WMS overlay: proxied raster tiles, optionally time-based (AROME). */
export interface WmsLayerDef {
  kind: 'wms'
  key: WeatherLayerKey
  /** European-Portuguese label shown in the control. */
  label: string
  /** WMS layer base name; the region suffix is appended per domain. */
  base: string
  /** AROME products are time-based; the fire risk product is not. */
  timeBased: boolean
  /** Domains that publish this layer. */
  regions: WeatherRegion[]
  /** GetLegendGraphic URL (loads in a plain <img>, no CORS needed). */
  legendUrl: string
}

/** RainViewer precipitation radar: animated client-side, no WMS metadata. */
export interface RadarLayerDef {
  kind: 'radar'
  key: 'radar'
  /** European-Portuguese label shown in the control. */
  label: string
}

export type WeatherLayerDef = WmsLayerDef | RadarLayerDef

const CONTINENT_ONLY: WeatherRegion[] = ['continent']
const ALL_REGIONS: WeatherRegion[] = ['continent', 'madeira', 'azores']

/** Legend graphic for a concrete WMS layer name (always the continent variant). */
function legendUrl(layer: string): string {
  return `https://mf2.ipma.pt/services?version=1.3.0&service=WMS&request=GetLegendGraphic&sld_version=1.1.0&layer=${layer}&format=image/png&STYLE=default`
}

/** Concrete def type per key, so indexing narrows to the right shape. */
interface WeatherLayerDefs {
  risk: WmsLayerDef
  radar: RadarLayerDef
  temperature: WmsLayerDef
  wind: WmsLayerDef
  gusts: WmsLayerDef
  humidity: WmsLayerDef
}

export const WEATHER_LAYERS: WeatherLayerDefs = {
  risk: {
    kind: 'wms',
    key: 'risk',
    label: 'Risco de incêndio',
    base: 'lsasaf.risk',
    timeBased: false,
    regions: CONTINENT_ONLY,
    legendUrl: legendUrl('lsasaf.risk.continent'),
  },
  radar: {
    kind: 'radar',
    key: 'radar',
    label: 'Radar (precipitação)',
  },
  temperature: {
    kind: 'wms',
    key: 'temperature',
    label: 'Temperatura',
    base: 'arome.2m.temperature',
    timeBased: true,
    regions: ALL_REGIONS,
    legendUrl: legendUrl('arome.2m.temperature.continent'),
  },
  wind: {
    kind: 'wms',
    key: 'wind',
    label: 'Vento',
    base: 'arome.10m.windintensity',
    timeBased: true,
    regions: ALL_REGIONS,
    legendUrl: legendUrl('arome.10m.windintensity.continent'),
  },
  gusts: {
    kind: 'wms',
    key: 'gusts',
    label: 'Rajadas de vento',
    base: 'arome.10m.gustintensity',
    timeBased: true,
    regions: ALL_REGIONS,
    legendUrl: legendUrl('arome.10m.gustintensity.continent'),
  },
  humidity: {
    kind: 'wms',
    key: 'humidity',
    label: 'Humidade relativa',
    base: 'arome.2m.relative_humidity',
    timeBased: true,
    regions: ALL_REGIONS,
    legendUrl: legendUrl('arome.2m.relative_humidity.continent'),
  },
}

/** Ordered list for rendering the control (matches the spec's label order). */
export const WEATHER_LAYER_LIST: WeatherLayerDef[] = [
  WEATHER_LAYERS.risk,
  WEATHER_LAYERS.radar,
  WEATHER_LAYERS.temperature,
  WEATHER_LAYERS.wind,
  WEATHER_LAYERS.gusts,
  WEATHER_LAYERS.humidity,
]

/** The off option shown at the top of the control. */
export const WEATHER_OFF_LABEL = 'Nenhuma'
export const WEATHER_CONTROL_TITLE = 'Camadas'
export const WEATHER_CREDIT = 'Dados: IPMA, I.P.'
export const RADAR_CREDIT = 'Radar: RainViewer.com'
export const WIND_PARTICLES_CREDIT = 'Partículas: Open-Meteo.com'

/**
 * Every concrete WMS layer name across all layers and regions. The tile proxy
 * validates each requested layer against this allowlist and rejects anything
 * else, so nothing but IPMA weather layers can be proxied.
 */
export const ALL_WMS_LAYERS: ReadonlySet<string> = new Set(
  WEATHER_LAYER_LIST.filter(
    (def): def is WmsLayerDef => def.kind === 'wms',
  ).flatMap((def) => def.regions.map((region) => `${def.base}.${region}`)),
)

/**
 * Concrete WMS layer names for a layer, restricted to the domains currently
 * known to be live. Continent is always included for the fire risk layer.
 */
export function wmsLayersFor(
  def: WmsLayerDef,
  liveRegions: readonly string[],
): string[] {
  return def.regions
    .filter((region) => liveRegions.includes(region))
    .map((region) => `${def.base}.${region}`)
}
