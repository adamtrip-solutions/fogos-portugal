import { useCallback, useEffect, useMemo, useRef } from 'react'
import { Layer, Map, Source } from 'react-map-gl/maplibre'
import { Minus, Plus } from 'lucide-react'

import type { FeatureCollection, Geometry } from 'geojson'
import type { MapLayerMouseEvent, MapRef } from 'react-map-gl/maplibre'
import type {
  CircleLayerSpecification,
  LineLayerSpecification,
} from 'react-map-gl/maplibre'
import type { Theme } from '#/lib/theme.ts'
import type { AircraftPosition, FleetAircraft } from '#/lib/fogos/types.ts'

const LIGHT_STYLE = 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json'
const DARK_STYLE = 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json'

// Matches the app's aerial-resources color (resource-chart chartConfig.aerial).
const ACTIVE_COLOR = '#2563EB'
const IDLE_COLOR = '#9CA3AF'

const SOURCE_ID = 'aircraft'
const HIT_LAYER = 'aircraft-dots'

interface AircraftFeatureProps {
  icao: string
  active: boolean
  selected: boolean
}

interface AircraftMapProps {
  aircraft: FleetAircraft[]
  selectedIcao: string | null
  onSelect: (icao: string | null) => void
  /** Recent track points of the selected aircraft (newest-first from the API). */
  track: AircraftPosition[] | undefined
  theme: Theme
}

function buildFleetCollection(
  aircraft: FleetAircraft[],
  selectedIcao: string | null,
): FeatureCollection<Geometry, AircraftFeatureProps> {
  const features = aircraft
    .filter((a) => a.position != null)
    .map((a) => ({
      type: 'Feature' as const,
      geometry: {
        type: 'Point' as const,
        coordinates: [
          a.position!.position.longitude,
          a.position!.position.latitude,
        ],
      },
      properties: {
        icao: a.tracked.icao,
        active: a.active,
        selected: a.tracked.icao === selectedIcao,
      },
    }))

  // Active aircraft (and the selection) paint last so they sit on top.
  features.sort((x, y) => {
    const rank = (p: AircraftFeatureProps) =>
      (p.active ? 1 : 0) + (p.selected ? 2 : 0)
    return rank(x.properties) - rank(y.properties)
  })

  return { type: 'FeatureCollection', features }
}

/** Oldest → newest LineString for the selected aircraft's track. */
function buildTrackCollection(
  track: AircraftPosition[] | undefined,
): FeatureCollection<Geometry> {
  const points = [...(track ?? [])].sort(
    (a, b) => Date.parse(a.sampledAt) - Date.parse(b.sampledAt),
  )
  const features =
    points.length >= 2
      ? [
          {
            type: 'Feature' as const,
            geometry: {
              type: 'LineString' as const,
              coordinates: points.map((p) => [
                p.position.longitude,
                p.position.latitude,
              ]),
            },
            properties: {},
          },
        ]
      : []
  return { type: 'FeatureCollection', features }
}

export function AircraftMap({
  aircraft,
  selectedIcao,
  onSelect,
  track,
  theme,
}: AircraftMapProps) {
  const mapRef = useRef<MapRef | null>(null)

  const data = useMemo(
    () => buildFleetCollection(aircraft, selectedIcao),
    [aircraft, selectedIcao],
  )
  const trackData = useMemo(() => buildTrackCollection(track), [track])

  // Fly to the selected aircraft when the selection changes (or when its
  // position first arrives). Deselection leaves the camera where it is.
  useEffect(() => {
    const map = mapRef.current?.getMap()
    if (!map || selectedIcao == null) return
    const selected = aircraft.find((a) => a.tracked.icao === selectedIcao)
    const pos = selected?.position
    if (!pos) return
    map.flyTo({
      center: [pos.position.longitude, pos.position.latitude],
      zoom: Math.max(map.getZoom(), 9),
      duration: 900,
      essential: true,
    })
  }, [selectedIcao, aircraft])

  const handleClick = useCallback(
    (event: MapLayerMouseEvent) => {
      const icao = event.features?.[0]?.properties?.icao
      if (typeof icao !== 'string') {
        onSelect(null)
        return
      }
      // Second click on the same aircraft deselects.
      onSelect(icao === selectedIcao ? null : icao)
    },
    [onSelect, selectedIcao],
  )

  const handleMouseMove = useCallback((event: MapLayerMouseEvent) => {
    const map = mapRef.current?.getMap()
    if (map) map.getCanvas().style.cursor = event.features?.[0] ? 'pointer' : ''
  }, [])

  const zoomBy = (delta: number) => {
    const map = mapRef.current?.getMap()
    if (map) map.easeTo({ zoom: map.getZoom() + delta, duration: 250 })
  }

  const dotPaint: CircleLayerSpecification['paint'] = {
    'circle-radius': [
      '+',
      ['case', ['get', 'active'], 7, 5],
      ['case', ['boolean', ['get', 'selected'], false], 2, 0],
    ],
    'circle-color': ['case', ['get', 'active'], ACTIVE_COLOR, IDLE_COLOR],
    'circle-opacity': 0.7,
    'circle-stroke-color': '#ffffff',
    'circle-stroke-width': [
      'case',
      ['boolean', ['get', 'selected'], false],
      3,
      1.5,
    ],
    'circle-stroke-opacity': 0.9,
  }

  const trackPaint: LineLayerSpecification['paint'] = {
    // Fade old → new along the path (needs lineMetrics on the source).
    'line-gradient': [
      'interpolate',
      ['linear'],
      ['line-progress'],
      0,
      'rgba(37, 99, 235, 0.12)',
      1,
      'rgba(37, 99, 235, 0.95)',
    ],
    'line-width': 2,
  }

  const trackLayout: LineLayerSpecification['layout'] = {
    'line-cap': 'round',
    'line-join': 'round',
  }

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
      interactiveLayerIds={[HIT_LAYER]}
      onClick={handleClick}
      onMouseMove={handleMouseMove}
      style={{ position: 'absolute', inset: 0 }}
    >
      {/* Selected aircraft's flight path, below the dots. */}
      <Source id="aircraft-track" type="geojson" data={trackData} lineMetrics>
        <Layer
          id="aircraft-track-line"
          type="line"
          layout={trackLayout}
          paint={trackPaint}
        />
      </Source>

      <Source id={SOURCE_ID} type="geojson" data={data}>
        <Layer id={HIT_LAYER} type="circle" paint={dotPaint} />
      </Source>

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
