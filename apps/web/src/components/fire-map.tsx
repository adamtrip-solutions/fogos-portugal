import {
  Suspense,
  lazy,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import { Layer, Map, Marker, Source } from 'react-map-gl/maplibre'
import { Camera, Minus, Plus } from 'lucide-react'

import type { FeatureCollection, Geometry } from 'geojson'
import type {
  MapLayerMouseEvent,
  MapRef,
} from 'react-map-gl/maplibre'
import type {
  CircleLayerSpecification,
  FillLayerSpecification,
  LineLayerSpecification,
  RasterLayerSpecification,
  SymbolLayerSpecification,
} from 'react-map-gl/maplibre'
import {
  isActiveStatus,
  statusBucket,
  statusColorForCode,
} from '#/lib/fogos/format.ts'
import type { StatusBucket } from '#/lib/fogos/format.ts'
import {
  addMissingMarkerImage,
  ensureMarkerImages,
} from '#/lib/fogos/marker-icons.ts'
import type { Theme } from '#/lib/theme.ts'
import type { IncidentListItem } from '#/lib/fogos/types.ts'
import { WEATHER_LAYERS, wmsLayersFor } from '#/lib/weather/catalog.ts'
import type { WeatherLayerKey } from '#/lib/weather/catalog.ts'
import type { WeatherAvailability } from '#/lib/weather/api.ts'
import { radarTileUrl } from '#/lib/weather/radar.ts'
import type { RadarData } from '#/lib/weather/radar.ts'
import type { WindField } from '#/lib/weather/wind.ts'

// The particle overlay drags in deck.gl/luma.gl/weatherlayers-gl (WebGL only),
// so it is loaded lazily: this keeps those deps out of the SSR module graph
// (fire-map.tsx is imported on the server) and out of the main client chunk.
const WindParticles = lazy(() => import('#/components/wind-particles.tsx'))

const LIGHT_STYLE = 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json'
const DARK_STYLE = 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json'

const SOURCE_ID = 'fires'

// Radius (CSS px) of the invisible `fires-hit` circle target around each badge
// centre. Touch pointers get a larger target than a precise mouse cursor.
const HIT_RADIUS_MOUSE = 24
const HIT_RADIUS_TOUCH = 32

// Pointer type never changes mid-session, so the hit radius is resolved once,
// lazily, on first use and cached. SSR-safe: matchMedia only exists in the
// browser, so on the server we fall back to the mouse radius (the layer paint is
// only built client-side anyway).
let hitRadiusCache: number | null = null
function resolveHitRadius(): number {
  if (hitRadiusCache != null) return hitRadiusCache
  const coarse =
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(pointer: coarse)').matches
  hitRadiusCache = coarse ? HIT_RADIUS_TOUCH : HIT_RADIUS_MOUSE
  return hitRadiusCache
}

// The pulsing halo is split into two circle layers with STATIC filters so the
// rAF loop can set a constant circle-radius / circle-opacity per layer (a plain
// uniform update) instead of a per-frame data-driven ['case', ['get',…]]
// expression, which forces a per-feature paint recompute + GPU buffer re-upload
// every frame. Both keep the original `active` gating; escalating fires get the
// faster/larger cycle. Hoisted to module scope so they never re-allocate.
const HALO_FILTER_BASE: CircleLayerSpecification['filter'] = [
  'all',
  ['==', ['get', 'active'], true],
  ['!=', ['get', 'escalating'], true],
]
const HALO_FILTER_ESCALATING: CircleLayerSpecification['filter'] = [
  'all',
  ['==', ['get', 'active'], true],
  ['==', ['get', 'escalating'], true],
]

interface FireFeatureProps {
  id: string
  color: string
  bucket: StatusBucket
  active: boolean
  important: boolean
  escalating: boolean
  label: string
  location: string
}

/** A hotspot visible on the map at the current scrub instant. */
export interface MapHotspot {
  id: string
  lng: number
  lat: number
  /** 0 (oldest) .. 1 (freshest) — drives the fade. */
  recency: number
}

/** A geotagged incident photo placed on the map. */
export interface MapPhoto {
  id: string
  lng: number
  lat: number
  url: string
}

/**
 * Per-incident map overlays derived in the panel and threaded down through
 * index.tsx (mirrors how the weather layers are passed in). Null when no
 * incident is selected.
 */
export interface IncidentMapOverlays {
  hotspots: MapHotspot[]
  perimeter: FeatureCollection<Geometry> | null
  photos: MapPhoto[]
}

interface PointGeometry {
  type: 'Point'
  coordinates: [number, number]
}

interface FireFeature {
  type: 'Feature'
  id?: number
  geometry: PointGeometry
  properties: FireFeatureProps
}

interface FireCollection {
  type: 'FeatureCollection'
  features: FireFeature[]
}

interface FireMapProps {
  incidents: IncidentListItem[]
  selectedId: string | null
  /**
   * Coordinates of the selected incident, threaded from index.tsx so the fly-to
   * works even for a deep-linked fire that is not in `incidents` (resolved via
   * the detail fetch). Falls back to the matching list item when null.
   */
  selectedCoordinates: { latitude: number; longitude: number } | null
  onSelect: (id: string | null) => void
  theme: Theme
  weatherLayer: WeatherLayerKey | 'none'
  weatherAvailability: WeatherAvailability | undefined
  radarData: RadarData | undefined
  radarActiveIndex: number
  windFields: WindField[] | undefined
  incidentOverlays: IncidentMapOverlays | null
}

const weatherRasterPaint: RasterLayerSpecification['paint'] = {
  'raster-opacity': 0.65,
  'raster-resampling': 'linear',
}

interface WeatherSource {
  key: string
  tiles: string
}

/**
 * Builds the proxied WMS tile template for the active weather layer, or null
 * when nothing should render (off, or an AROME layer with no live run). The
 * `{bbox-epsg-3857}` token is left intact for MapLibre to substitute per tile.
 */
function buildWeatherSource(
  weatherLayer: WeatherLayerKey | 'none',
  availability: WeatherAvailability | undefined,
): WeatherSource | null {
  if (weatherLayer === 'none') return null
  const def = WEATHER_LAYERS[weatherLayer]
  // Radar is animated client-side (see the radar sources below), not proxied.
  if (def.kind !== 'wms') return null

  if (def.timeBased) {
    const referenceTime = availability?.referenceTime
    const time = availability?.time
    if (!referenceTime || !time) return null

    const liveRegions = availability?.regions ?? ['continent']
    const layers = wmsLayersFor(def, liveRegions)
    if (layers.length === 0) return null

    const tiles =
      `/api/weather-tiles?layers=${layers.join(',')}` +
      `&bbox={bbox-epsg-3857}&time=${time}&reference_time=${referenceTime}`
    return { key: `${weatherLayer}|${referenceTime}|${time}`, tiles }
  }

  // Fire risk: no time parameters, continent only, always available.
  const layers = wmsLayersFor(def, ['continent'])
  const tiles = `/api/weather-tiles?layers=${layers.join(',')}&bbox={bbox-epsg-3857}`
  return { key: `${weatherLayer}|static`, tiles }
}

function buildFeatureCollection(incidents: IncidentListItem[]): FireCollection {
  const features = incidents
    .filter((i) => i.coordinates != null)
    .map((incident) => {
      const numericId = Number(incident.id)
      const feature: FireFeature = {
        type: 'Feature',
        id: Number.isSafeInteger(numericId) ? numericId : undefined,
        geometry: {
          type: 'Point',
          coordinates: [
            incident.coordinates!.longitude,
            incident.coordinates!.latitude,
          ],
        },
        properties: {
          id: incident.id,
          color: statusColorForCode(incident.status.code),
          bucket: statusBucket(incident.status.code),
          active: isActiveStatus(incident.status.code),
          important: incident.important,
          escalating: incident.signals.escalating,
          label: incident.status.label,
          location: incident.location,
        },
      }
      return feature
    })

  return { type: 'FeatureCollection', features }
}

export function FireMap({
  incidents,
  selectedId,
  selectedCoordinates,
  onSelect,
  theme,
  weatherLayer,
  weatherAvailability,
  radarData,
  radarActiveIndex,
  windFields,
  incidentOverlays,
}: FireMapProps) {
  const mapRef = useRef<MapRef | null>(null)
  const hoveredIdRef = useRef<number | null>(null)
  const selectedFeatureIdRef = useRef<number | null>(null)
  const [mapLoaded, setMapLoaded] = useState(false)

  // The invisible `fires-hit` circle layer's paint is built once on mount: its
  // radius is a session-constant, so it never needs to react to renders.
  const [hitPaint] = useState<CircleLayerSpecification['paint']>(() => ({
    'circle-radius': resolveHitRadius(),
    'circle-opacity': 0,
    'circle-stroke-width': 0,
  }))

  const data = useMemo(() => buildFeatureCollection(incidents), [incidents])

  const weatherSource = useMemo(
    () => buildWeatherSource(weatherLayer, weatherAvailability),
    [weatherLayer, weatherAvailability],
  )

  // Merged VIIRS+MODIS hotspots (already filtered to ≤ scrub time in the panel),
  // as a GeoJSON collection carrying a `recency` prop for the fade expression.
  const hotspotData = useMemo<FeatureCollection<Geometry>>(() => {
    const points = incidentOverlays?.hotspots ?? []
    return {
      type: 'FeatureCollection',
      features: points.map((h) => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [h.lng, h.lat] },
        properties: { recency: h.recency },
      })),
    }
  }, [incidentOverlays])

  // Radar frames render only for the radar layer; every frame stays mounted so
  // its tiles prefetch, and opacity crossfades to the active one.
  const radarFrames =
    weatherLayer === 'radar' ? (radarData?.frames ?? []) : []
  // Wind particles ride over the IPMA windintensity raster.
  const showWindParticles =
    weatherLayer === 'wind' && windFields != null && windFields.length > 0

  const numericIdFor = useCallback(
    (id: string | null): number | null => {
      if (id == null) return null
      const feature = data.features.find((f) => f.properties.id === id)
      return typeof feature?.id === 'number' ? feature.id : null
    },
    [data],
  )

  // Kept current for the styleimagemissing listener, which re-adds badge
  // images after a basemap swap (theme toggle) with the live theme.
  const themeRef = useRef(theme)
  useEffect(() => {
    themeRef.current = theme
  }, [theme])

  // --- Pulsing halo: one rAF loop drives radius + opacity. Escalating fires
  // pulse on a faster, larger cycle so they read as urgent. Each cycle is a
  // separate circle layer with a static filter, so we set a CONSTANT number per
  // layer — a plain uniform update — rather than a per-frame data-driven
  // ['case', ['get', 'escalating'], …] expression (which would recompute paint
  // per feature and re-upload the GPU buffer every frame). --------------------
  useEffect(() => {
    let frame = 0
    const CYCLE_MS = 2200
    const FAST_CYCLE_MS = 1100

    const tick = () => {
      const map = mapRef.current?.getMap()
      if (map && !document.hidden) {
        const now = performance.now()
        const t = (now % CYCLE_MS) / CYCLE_MS
        const s = (Math.sin(t * Math.PI * 2) + 1) / 2 // 0..1
        const radius = 16 + s * 14 // 16..30 — sized for the bigger badges
        const opacity = 0.35 * (1 - s) // 0.35..0

        const tf = (now % FAST_CYCLE_MS) / FAST_CYCLE_MS
        const sf = (Math.sin(tf * Math.PI * 2) + 1) / 2
        const radiusEsc = 18 + sf * 22 // 18..40 — larger sweep
        const opacityEsc = 0.5 * (1 - sf) // 0.5..0 — bolder

        if (map.getLayer('fires-halo')) {
          map.setPaintProperty('fires-halo', 'circle-radius', radius)
          map.setPaintProperty('fires-halo', 'circle-opacity', opacity)
        }
        if (map.getLayer('fires-halo-escalating')) {
          map.setPaintProperty('fires-halo-escalating', 'circle-radius', radiusEsc)
          map.setPaintProperty('fires-halo-escalating', 'circle-opacity', opacityEsc)
        }
      }
      frame = requestAnimationFrame(tick)
    }

    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [])

  // --- Keep the selected feature-state in sync with data + selection. ------
  useEffect(() => {
    const map = mapRef.current?.getMap()
    if (!map || !map.getSource(SOURCE_ID)) return

    const prev = selectedFeatureIdRef.current
    if (prev != null) {
      map.removeFeatureState({ source: SOURCE_ID, id: prev }, 'selected')
    }
    const next = numericIdFor(selectedId)
    selectedFeatureIdRef.current = next
    if (next != null) {
      map.setFeatureState({ source: SOURCE_ID, id: next }, { selected: true })
    }
  }, [selectedId, numericIdFor])

  // --- Fly to the selected incident; clear padding when it closes. ----------
  // `flownForRef` records which selection we've already flown to, so: (a) a
  // deep-linked fire whose coordinates only arrive after a fetch flies exactly
  // once, (b) a background poll refetch never re-flies the current selection,
  // and (c) picking a different fire flies again. Gated on `mapLoaded` so an
  // initial ?incident=ID deep link waits for the style before flying.
  const flownForRef = useRef<string | null>(null)
  useEffect(() => {
    const map = mapRef.current?.getMap()
    if (!map || !mapLoaded) return

    if (selectedId == null) {
      flownForRef.current = null
      map.easeTo({
        padding: { top: 0, right: 0, bottom: 0, left: 0 },
        duration: 500,
      })
      return
    }

    if (flownForRef.current === selectedId) return

    const coords =
      incidents.find((i) => i.id === selectedId)?.coordinates ??
      selectedCoordinates
    if (!coords) return // coordinates not known yet — re-runs when they arrive

    flownForRef.current = selectedId
    const isMobile = window.matchMedia('(max-width: 767px)').matches
    map.flyTo({
      center: [coords.longitude, coords.latitude],
      zoom: Math.max(map.getZoom(), 11),
      duration: 900,
      essential: true,
      padding: isMobile
        ? { top: 0, right: 0, bottom: window.innerHeight * 0.45, left: 0 }
        : { top: 0, right: 420, bottom: 0, left: 0 },
    })
  }, [selectedId, selectedCoordinates, incidents, mapLoaded])

  // Re-applied on load AND on styledata: swapping the basemap (theme toggle)
  // rebuilds the style and wipes all feature-state.
  const applySelectedState = useCallback(() => {
    const map = mapRef.current?.getMap()
    if (!map || !map.getSource(SOURCE_ID)) return
    const next = numericIdFor(selectedId)
    selectedFeatureIdRef.current = next
    if (next != null) {
      map.setFeatureState({ source: SOURCE_ID, id: next }, { selected: true })
    }
  }, [numericIdFor, selectedId])

  // On load: register the badge images and a styleimagemissing listener that
  // re-adds them after a basemap style swap (theme toggle) wipes them.
  const handleLoad = useCallback(() => {
    const map = mapRef.current?.getMap()
    if (!map) return
    ensureMarkerImages(map, themeRef.current)
    map.on('styleimagemissing', (e) => {
      addMissingMarkerImage(map, e.id, themeRef.current)
    })
    applySelectedState()
    setMapLoaded(true)
  }, [applySelectedState])

  // On styledata: if style diffing preserved the images across a theme swap,
  // the ring color is stale — ensureMarkerImages regenerates for the live
  // theme (idempotent otherwise). styleimagemissing only covers the wiped case.
  const handleStyleData = useCallback(() => {
    const map = mapRef.current?.getMap()
    if (map) {
      try {
        ensureMarkerImages(map, themeRef.current)
      } catch {
        // Style still loading — styleimagemissing will backfill.
      }
    }
    applySelectedState()
  }, [applySelectedState])

  // Hit-testing runs natively against the invisible `fires-hit` CIRCLE layer
  // (see interactiveLayerIds + the layer's comment). MapLibre feeds the hovered/
  // clicked feature via event.features, so click and hover both just read
  // event.features[0] — no screen-space projection sweep needed.
  const handleClick = useCallback(
    (event: MapLayerMouseEvent) => {
      // properties.id is the incident's string id (feature.id is the numeric
      // form used for feature-state); onSelect expects the string id.
      const feature = event.features?.[0]
      const id = feature?.properties?.id
      onSelect(typeof id === 'string' ? id : null)
    },
    [onSelect],
  )

  const handleMouseMove = useCallback((event: MapLayerMouseEvent) => {
    const map = mapRef.current?.getMap()
    if (!map) return

    const feature = event.features?.[0]
    map.getCanvas().style.cursor = feature ? 'pointer' : ''

    const rawId = feature?.id
    const nextHover = typeof rawId === 'number' ? rawId : null

    if (hoveredIdRef.current === nextHover) return
    if (hoveredIdRef.current != null) {
      map.removeFeatureState(
        { source: SOURCE_ID, id: hoveredIdRef.current },
        'hover',
      )
    }
    hoveredIdRef.current = nextHover
    if (nextHover != null) {
      map.setFeatureState({ source: SOURCE_ID, id: nextHover }, { hover: true })
    }
  }, [])

  const handleMouseLeave = useCallback(() => {
    const map = mapRef.current?.getMap()
    if (!map) return
    map.getCanvas().style.cursor = ''
    if (hoveredIdRef.current != null) {
      map.removeFeatureState(
        { source: SOURCE_ID, id: hoveredIdRef.current },
        'hover',
      )
      hoveredIdRef.current = null
    }
  }, [])

  const zoomBy = (delta: number) => {
    const map = mapRef.current?.getMap()
    if (!map) return
    map.easeTo({ zoom: map.getZoom() + delta, duration: 250 })
  }

  // Two halo layers, one per pulse cycle. The rAF loop overwrites circle-radius
  // and circle-opacity every frame with constant numbers; these are just the
  // pre-animation seeds (start of each cycle).
  const haloPaint: CircleLayerSpecification['paint'] = {
    'circle-color': ['get', 'color'],
    'circle-radius': 16,
    'circle-opacity': 0.35,
    'circle-blur': 0.25,
  }
  const haloPaintEscalating: CircleLayerSpecification['paint'] = {
    'circle-color': ['get', 'color'],
    'circle-radius': 18,
    'circle-opacity': 0.5,
    'circle-blur': 0.25,
  }

  // Hover / selected affordance drawn as a bare ring around the badge, since a
  // symbol layer's icon-size can't read feature-state.
  const ringPaint: CircleLayerSpecification['paint'] = {
    'circle-radius': ['case', ['get', 'important'], 21, 18],
    'circle-color': '#000000', // never shown — opacity 0, stroke only
    'circle-opacity': 0,
    'circle-stroke-color': ['get', 'color'],
    'circle-stroke-width': [
      'case',
      ['boolean', ['feature-state', 'selected'], false],
      3,
      ['boolean', ['feature-state', 'hover'], false],
      2,
      0,
    ],
    'circle-stroke-opacity': [
      'case',
      ['boolean', ['feature-state', 'selected'], false],
      1,
      ['boolean', ['feature-state', 'hover'], false],
      0.6,
      0,
    ],
  }

  const badgeLayout: SymbolLayerSpecification['layout'] = {
    // bucket + important -> one of the 10 registered image names.
    'icon-image': [
      'concat',
      'badge-',
      ['get', 'bucket'],
      ['case', ['get', 'important'], '-important', ''],
    ],
    // 60px canvas @ pixelRatio 2 == 30 logical px at size 1. Grow slightly with
    // zoom. Markers must never drop out to collision.
    'icon-size': ['interpolate', ['linear'], ['zoom'], 5, 1.0, 12, 1.15],
    'icon-allow-overlap': true,
    'icon-ignore-placement': true,
    // No clustering: when badges overlap, draw the more relevant fire on top.
    // Higher sort key renders later, i.e. on top. Active (and important) fires
    // stay on top; among the finished-ish states, resolving (still on the
    // ground) beats vigilância, which beats concluded.
    'symbol-sort-key': [
      '+',
      ['case', ['get', 'active'], 6, 0],
      ['case', ['get', 'important'], 3, 0],
      ['match', ['get', 'bucket'], 'resolving', 2, 'vigilancia', 1, 0],
    ],
  }

  const badgePaint: SymbolLayerSpecification['paint'] = {
    'icon-opacity': ['case', ['get', 'active'], 1, 0.9],
  }

  // Hotspots: recency 0 (old, dark ember) → 1 (fresh, bright yellow), fading in.
  const hotspotPaint: CircleLayerSpecification['paint'] = {
    'circle-radius': ['interpolate', ['linear'], ['zoom'], 6, 2.5, 12, 5],
    'circle-color': [
      'interpolate',
      ['linear'],
      ['get', 'recency'],
      0,
      '#7c2d12',
      0.5,
      '#ea580c',
      1,
      '#fbbf24',
    ],
    'circle-opacity': [
      'interpolate',
      ['linear'],
      ['get', 'recency'],
      0,
      0.35,
      1,
      0.9,
    ],
    'circle-blur': 0.3,
    'circle-stroke-width': 0,
  }

  const perimeterFillPaint: FillLayerSpecification['paint'] = {
    'fill-color': '#f97316',
    'fill-opacity': 0.12,
  }

  const perimeterLinePaint: LineLayerSpecification['paint'] = {
    'line-color': '#ea580c',
    'line-width': 2,
    'line-opacity': 0.9,
  }

  const perimeter = incidentOverlays?.perimeter ?? null
  const photos = incidentOverlays?.photos ?? []

  return (
    <Map
      ref={mapRef}
      mapStyle={theme === 'dark' ? DARK_STYLE : LIGHT_STYLE}
      initialViewState={{
        bounds: [
          [-9.6, 36.8],
          [-6.1, 42.2],
        ],
        fitBoundsOptions: { padding: 48 },
      }}
      attributionControl={{ compact: true }}
      interactiveLayerIds={['fires-hit']}
      onLoad={handleLoad}
      onStyleData={handleStyleData}
      onClick={handleClick}
      onMouseMove={handleMouseMove}
      onMouseLeave={handleMouseLeave}
      style={{ position: 'absolute', inset: 0 }}
    >
      <Source id={SOURCE_ID} type="geojson" data={data}>
        <Layer
          id="fires-halo"
          type="circle"
          filter={HALO_FILTER_BASE}
          paint={haloPaint}
        />
        <Layer
          id="fires-halo-escalating"
          type="circle"
          filter={HALO_FILTER_ESCALATING}
          paint={haloPaintEscalating}
        />
        <Layer id="fires-ring" type="circle" paint={ringPaint} />
        <Layer
          id="fires-badge"
          type="symbol"
          layout={badgeLayout}
          paint={badgePaint}
        />
        {/*
          Invisible CIRCLE hit-target layer — the ONLY interactive fire layer
          (see interactiveLayerIds). Do NOT delete or fold this back into the
          `fires-badge` symbol layer.

          History: badges are a SYMBOL layer, and MapLibre's hit-testing for
          symbols (queryRenderedFeatures, which feeds event.features) depends on
          the placement/collision index. That index is recomputed lazily after
          camera moves, so during/just after an animation (fly-to on select,
          radar frame repaints) a click/hover at a plainly-visible badge could
          miss — the fire would deselect until a manual zoom forced re-placement.

          CIRCLE layers have no placement index: they are hit-tested
          geometrically from the tile features against the current transform, so
          they can never go stale. This transparent circle rides on the same
          source, above the badge (so it wins hit priority), giving a touch-
          friendly, animation-proof hit target while the badge stays purely
          visual. Radius is a session constant (mouse vs. coarse pointer).
        */}
        <Layer id="fires-hit" type="circle" paint={hitPaint} />
      </Source>

      {/* Perimeter history (selected KML version → GeoJSON), below the fire
          markers. Fill + outline. */}
      {perimeter && (
        <Source id="incident-perimeter" type="geojson" data={perimeter}>
          <Layer
            id="incident-perimeter-fill"
            type="fill"
            beforeId="fires-halo"
            paint={perimeterFillPaint}
          />
          <Layer
            id="incident-perimeter-line"
            type="line"
            beforeId="fires-halo"
            paint={perimeterLinePaint}
          />
        </Source>
      )}

      {/* Spread hotspots (VIIRS+MODIS) up to the scrubber time, faded by recency. */}
      <Source id="incident-hotspots" type="geojson" data={hotspotData}>
        <Layer
          id="incident-hotspots-layer"
          type="circle"
          beforeId="fires-halo"
          paint={hotspotPaint}
        />
      </Source>

      {/* Geotagged incident photos as camera pins; click opens the image. */}
      {photos.map((photo) => (
        <Marker
          key={photo.id}
          longitude={photo.lng}
          latitude={photo.lat}
          anchor="bottom"
        >
          <button
            type="button"
            aria-label="Abrir fotografia"
            onClick={() => window.open(photo.url, '_blank', 'noopener')}
            className="flex size-6 items-center justify-center rounded-full border border-white bg-zinc-900/85 text-white shadow-md transition-transform hover:scale-110 dark:border-zinc-800 dark:bg-white/90 dark:text-zinc-900"
          >
            <Camera className="size-3.5" />
          </button>
        </Marker>
      ))}

      {/* Rendered after the fires source; beforeId slots it below the fires
          layers, which react-map-gl v8 reconciles reactively. */}
      {weatherSource && (
        <Source
          key={weatherSource.key}
          id="weather"
          type="raster"
          tiles={[weatherSource.tiles]}
          tileSize={256}
          attribution="Dados: IPMA, I.P."
        >
          <Layer
            id="weather-raster"
            type="raster"
            beforeId="fires-halo"
            paint={weatherRasterPaint}
          />
        </Source>
      )}

      {/* RainViewer radar: one source per frame, active frame faded in. */}
      {radarData &&
        radarFrames.map((frame, i) => (
          <Source
            key={frame.time}
            id={`radar-${frame.time}`}
            type="raster"
            tiles={[radarTileUrl(radarData.host, frame)]}
            tileSize={256}
            maxzoom={7}
            attribution="RainViewer.com"
          >
            <Layer
              id={`radar-raster-${frame.time}`}
              type="raster"
              beforeId="fires-halo"
              paint={{
                'raster-opacity': i === radarActiveIndex ? 0.7 : 0,
                'raster-opacity-transition': { duration: 150, delay: 0 },
                'raster-fade-duration': 0,
              }}
            />
          </Source>
        ))}

      {/* Animated wind particles, lazily loaded (WebGL-only, SSR-unsafe). */}
      {showWindParticles && (
        <Suspense fallback={null}>
          <WindParticles fields={windFields} theme={theme} />
        </Suspense>
      )}

      <div className="absolute bottom-8 right-2.5 z-10 flex flex-col overflow-hidden rounded-xl border border-black/5 bg-white/75 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70">
        <button
          type="button"
          aria-label="Aproximar"
          onClick={() => zoomBy(1)}
          className="flex size-9 items-center justify-center text-zinc-700 transition-colors hover:bg-black/5 dark:text-zinc-200 dark:hover:bg-white/10"
        >
          <Plus className="size-4" />
        </button>
        <span className="h-px w-full bg-black/10 dark:bg-white/10" aria-hidden />
        <button
          type="button"
          aria-label="Afastar"
          onClick={() => zoomBy(-1)}
          className="flex size-9 items-center justify-center text-zinc-700 transition-colors hover:bg-black/5 dark:text-zinc-200 dark:hover:bg-white/10"
        >
          <Minus className="size-4" />
        </button>
      </div>
    </Map>
  )
}
