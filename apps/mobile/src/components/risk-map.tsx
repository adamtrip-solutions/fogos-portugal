import { forwardRef, useImperativeHandle, useMemo, useRef } from 'react'
import { StyleSheet } from 'react-native'
import { Camera, GeoJSONSource, Layer, Map } from '@maplibre/maplibre-react-native'
import type {
  CameraRef,
  FillLayerSpecification,
  FilterSpecification,
  LineLayerSpecification,
} from '@maplibre/maplibre-react-native'
import type { FeatureCollection } from 'geojson'

import { RISK_LEVELS, RISK_STYLE, RISK_UNKNOWN } from '@fogos/ui-tokens'
import type { RiskFeatureCollection } from '@fogos/api-client'

// Keyless Carto basemaps — the same styles the fire map uses. Native has no
// CORS, so these load directly.
const LIGHT_STYLE = 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json'
const DARK_STYLE = 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json'

const SOURCE_ID = 'risk'

// Continental Portugal — same framing as the fire map.
const PORTUGAL_CENTER: [number, number] = [-8.2, 39.6]
const PORTUGAL_ZOOM = 5.4

// Zoom the camera settles on when a concelho is picked from search.
const FOCUS_ZOOM = 8.5
const FOCUS_DURATION_MS = 900

// level → fill colour, built once from the shared risk palette. Level 0 (no value
// for the horizon) and anything out of range fall through to the neutral swatch.
// Ported from web's FILL_COLOR_MATCH.
const FILL_COLOR_MATCH = [
  'match',
  ['get', 'level'],
  ...RISK_LEVELS.flatMap((l) => [l, RISK_STYLE[l].bg]),
  RISK_UNKNOWN.bg,
] as unknown as NonNullable<FillLayerSpecification['paint']>['fill-color']

const fillPaint: FillLayerSpecification['paint'] = {
  'fill-color': FILL_COLOR_MATCH,
  'fill-opacity': 0.75,
}

const selectedPaint: LineLayerSpecification['paint'] = {
  'line-color': '#ea580c',
  'line-width': 2.5,
}

/** Imperative handle: fly the camera to a concelho centroid (search selection). */
export interface RiskMapRef {
  focus: (center: [number, number]) => void
}

interface RiskMapProps {
  data: RiskFeatureCollection
  isDark: boolean
  selectedDico: string | null
  onSelect: (dico: string) => void
  /** Bottom safe-area inset (px) so the attribution button clears the legend/card. */
  bottomInset?: number
}

/**
 * The national fire-risk choropleth — the mobile port of web's `RiskMap`. A
 * GeoJSON fill layer coloured by concelho risk level, a subtle border line, and
 * a highlighted outline on the selected concelho. Taps resolve the concelho DICO
 * via `GeoJSONSource.onPress`.
 */
export const RiskMap = forwardRef<RiskMapRef, RiskMapProps>(function RiskMap(
  { data, isDark, selectedDico, onSelect, bottomInset = 0 },
  ref,
) {
  const cameraRef = useRef<CameraRef>(null)

  useImperativeHandle(
    ref,
    () => ({
      focus: (center) => {
        cameraRef.current?.flyTo({
          center,
          zoom: FOCUS_ZOOM,
          duration: FOCUS_DURATION_MS,
        })
      },
    }),
    [],
  )

  const borderPaint = useMemo<LineLayerSpecification['paint']>(
    () => ({
      'line-color': isDark ? 'rgba(255,255,255,0.18)' : 'rgba(0,0,0,0.18)',
      'line-width': 0.5,
    }),
    [isDark],
  )

  const selectedFilter: FilterSpecification = [
    '==',
    ['get', 'dico'],
    selectedDico ?? '__none__',
  ]

  return (
    <Map
      style={StyleSheet.absoluteFill}
      mapStyle={isDark ? DARK_STYLE : LIGHT_STYLE}
      attribution
      attributionPosition={{ bottom: bottomInset + 8, right: 8 }}
      logo={false}
      compass={false}
    >
      <Camera
        ref={cameraRef}
        initialViewState={{ center: PORTUGAL_CENTER, zoom: PORTUGAL_ZOOM }}
      />
      <GeoJSONSource
        id={SOURCE_ID}
        data={data as unknown as FeatureCollection}
        onPress={(event) => {
          const feature = event.nativeEvent.features?.[0]
          const dico = feature?.properties?.dico
          if (typeof dico === 'string' && dico.length > 0) onSelect(dico)
        }}
      >
        <Layer id="risk-fill" type="fill" paint={fillPaint} />
        <Layer id="risk-border" type="line" paint={borderPaint} />
        <Layer
          id="risk-selected"
          type="line"
          filter={selectedFilter}
          paint={selectedPaint}
        />
      </GeoJSONSource>
    </Map>
  )
})
