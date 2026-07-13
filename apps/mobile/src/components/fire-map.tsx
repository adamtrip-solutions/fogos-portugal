import { forwardRef, useImperativeHandle, useMemo, useRef } from 'react'
import { StyleSheet } from 'react-native'
import {
  Camera,
  GeoJSONSource,
  Layer,
  Map,
} from '@maplibre/maplibre-react-native'
import type {
  CameraRef,
  CircleLayerSpecification,
  FilterSpecification,
} from '@maplibre/maplibre-react-native'
import type { Feature, FeatureCollection, Point } from 'geojson'

import { isActiveStatus, statusBucket, statusColorForCode } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'
import type { Coordinates, IncidentListItem } from '@fogos/api-client'

// Keyless Carto basemaps — same styles the web map uses. Native has no CORS, so
// these load directly.
const LIGHT_STYLE = 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json'
const DARK_STYLE = 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json'

const SOURCE_ID = 'fires'
const HIT_LAYER_ID = 'fires-hit'

// Continental Portugal — center + zoom (matches the web map's initial framing).
const PORTUGAL_CENTER: [number, number] = [-8.2, 39.6]
const PORTUGAL_ZOOM = 5.4

// Zoom the deep-link camera settles on when focusing a single fire (plan 1.4).
const FOCUS_ZOOM = 9
const FOCUS_DURATION_MS = 1200

// Stacking severity when dots overlap — live fires on top, finished at the
// bottom. Ported verbatim from the web map so both surfaces agree.
const BUCKET_RANK: Record<StatusBucket, number> = {
  done: 0,
  vigilancia: 1,
  resolving: 2,
  dispatch: 3,
  ongoing: 4,
}

interface FireFeatureProps {
  id: string
  color: string
  bucket: StatusBucket
  priority: number
  active: boolean
  important: boolean
  escalating: boolean
}

function buildFeatureCollection(
  incidents: IncidentListItem[],
): FeatureCollection<Point, FireFeatureProps> {
  const features = incidents
    .filter((i) => i.coordinates != null)
    .map((incident): Feature<Point, FireFeatureProps> => {
      const bucket = statusBucket(incident.status.code)
      return {
        type: 'Feature',
        id: incident.id,
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
          bucket,
          // Severity outranks the flags: escalating/important only break ties
          // within a bucket, so a finished "important" fire never covers a live
          // one. `circle-sort-key` renders higher priority on top.
          priority:
            BUCKET_RANK[bucket] * 4 +
            (incident.signals.escalating ? 2 : 0) +
            (incident.important ? 1 : 0),
          active: isActiveStatus(incident.status.code),
          important: incident.important,
          escalating: incident.signals.escalating,
        },
      }
    })
  return { type: 'FeatureCollection', features }
}

// Soft attention halo behind escalating or "important" fires — the native
// stand-in for the web map's animated pulse. Bucket-colored, blurred, faint.
const HALO_FILTER: FilterSpecification = [
  'any',
  ['==', ['get', 'escalating'], true],
  ['==', ['get', 'important'], true],
]
const haloPaint: CircleLayerSpecification['paint'] = {
  'circle-color': ['get', 'color'],
  'circle-radius': ['case', ['get', 'escalating'], 22, 18],
  'circle-opacity': 0.28,
  'circle-blur': 0.6,
}

// The dot itself: bucket color with a white ring, sized up slightly with zoom.
const dotPaint: CircleLayerSpecification['paint'] = {
  'circle-color': ['get', 'color'],
  'circle-radius': ['interpolate', ['linear'], ['zoom'], 5, 6, 12, 9],
  'circle-stroke-color': '#ffffff',
  'circle-stroke-width': ['case', ['get', 'active'], 2, 1.5],
  'circle-opacity': ['case', ['get', 'active'], 1, 0.9],
}
const dotLayout: CircleLayerSpecification['layout'] = {
  // Higher priority renders on top — the native equivalent of the web's
  // source-order/symbol-sort-key stacking.
  'circle-sort-key': ['get', 'priority'],
}

interface FireMapProps {
  incidents: IncidentListItem[]
  isDark: boolean
  onSelect: (id: string) => void
  /**
   * Bottom safe/tab-bar inset (px). Offsets MapLibre's bottom-right attribution
   * button so the tab bar never overlaps it (the button is legally required).
   */
  bottomInset?: number
}

/** Imperative handle: fly the camera to a fire (used by deep-link selection). */
export interface FireMapRef {
  /** Animate the camera to center on a coordinate at the single-fire zoom. */
  focus: (coordinate: Coordinates) => void
}

export const FireMap = forwardRef<FireMapRef, FireMapProps>(function FireMap(
  { incidents, isDark, onSelect, bottomInset = 0 },
  ref,
) {
  const data = useMemo(() => buildFeatureCollection(incidents), [incidents])
  const cameraRef = useRef<CameraRef>(null)

  useImperativeHandle(ref, () => ({
    focus: ({ longitude, latitude }) => {
      cameraRef.current?.flyTo({
        center: [longitude, latitude],
        zoom: FOCUS_ZOOM,
        duration: FOCUS_DURATION_MS,
      })
    },
  }), [])

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
        data={data}
        onPress={(event) => {
          // The topmost-rendered feature (the hit layer wins) is features[0].
          const feature = event.nativeEvent.features?.[0]
          const id = feature?.properties?.id
          if (typeof id === 'string') onSelect(id)
        }}
      >
        <Layer id="fires-halo" type="circle" filter={HALO_FILTER} paint={haloPaint} />
        <Layer id="fires-dot" type="circle" layout={dotLayout} paint={dotPaint} />
        {/*
          Invisible, larger circle sitting on top gives a touch-friendly,
          animation-proof hit target while the visible dot stays small — the
          native mirror of the web map's `fires-hit` layer.
        */}
        <Layer
          id={HIT_LAYER_ID}
          type="circle"
          paint={{ 'circle-radius': 22, 'circle-opacity': 0 }}
        />
      </GeoJSONSource>
    </Map>
  )
})
