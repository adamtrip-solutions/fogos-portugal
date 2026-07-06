import type { Map as MapLibreMap } from 'maplibre-gl'

import { STATUS_BUCKET_COLOR } from './format.ts'
import type { StatusBucket } from './format.ts'
import type { Theme } from '../theme.ts'

/**
 * Canvas-rendered circular badge markers: a status-colored disc with a white
 * lucide icon, a theme-colored ring, and a soft shadow. Two sizes per bucket
 * (base / important), each drawn on a 2x canvas and registered with
 * `pixelRatio: 2` so a 60px canvas reads as 30 logical px on the map.
 *
 * Icon geometry is copied verbatim from lucide-react v0.577.0
 * (node_modules/lucide-react/dist/esm/icons/{siren,flame,droplet,eye,check}.js).
 * Lucide icons are 24x24 STROKE icons: stroke-width 2, round caps/joins, no
 * fill. All use only <path> nodes here (eye's <circle cx=12 cy=12 r=3> pupil is
 * re-expressed as the equivalent SVG-arc path), so Path2D covers them. We do
 * NOT import lucide-react here — the canvas path data is inlined below.
 */

// bucket -> lucide icon: dispatch=siren, ongoing=flame, resolving=droplet,
// vigilancia=eye, done=check.
const ICON_PATHS: Record<StatusBucket, string[]> = {
  // siren
  dispatch: [
    'M7 18v-6a5 5 0 1 1 10 0v6',
    'M5 21a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-1a2 2 0 0 0-2-2H7a2 2 0 0 0-2 2z',
    'M21 12h1',
    'M18.5 4.5 18 5',
    'M2 12h1',
    'M12 2v1',
    'm4.929 4.929.707.707',
    'M12 12v6',
  ],
  // flame
  ongoing: [
    'M12 3q1 4 4 6.5t3 5.5a1 1 0 0 1-14 0 5 5 0 0 1 1-3 1 1 0 0 0 5 0c0-2-1.5-3-1.5-5q0-2 2.5-4',
  ],
  // droplet
  resolving: [
    'M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5c-.5 2.5-2 4.9-4 6.5C6 11.1 5 13 5 15a7 7 0 0 0 7 7z',
  ],
  // eye (pupil <circle> re-expressed as an SVG-arc path)
  vigilancia: [
    'M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0',
    'M15 12a3 3 0 1 1-6 0 3 3 0 1 1 6 0',
  ],
  // check
  done: ['M20 6 9 17l-5-5'],
}

const BUCKETS: StatusBucket[] = [
  'dispatch',
  'ongoing',
  'resolving',
  'vigilancia',
  'done',
]

const PIXEL_RATIO = 2
const LOGICAL_BASE = 30
const LOGICAL_IMPORTANT = 36

/** Remembers the theme each map's images were rendered with, to detect swaps. */
const themeForMap = new WeakMap<MapLibreMap, Theme>()

function ringColor(theme: Theme): string {
  return theme === 'dark' ? '#18181b' : '#ffffff'
}

export function markerImageName(
  bucket: StatusBucket,
  important: boolean,
): string {
  return important ? `badge-${bucket}-important` : `badge-${bucket}`
}

/** Draws one badge at `logical` px on a 2x canvas and returns its ImageData. */
function renderBadge(
  bucket: StatusBucket,
  logical: number,
  theme: Theme,
): ImageData {
  const size = logical * PIXEL_RATIO
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('2D canvas context unavailable')

  const center = size / 2
  const radius = size / 2 - 5

  // 1 + 2. Disc, filled with a soft drop shadow.
  ctx.save()
  ctx.shadowColor = 'rgba(0,0,0,0.3)'
  ctx.shadowBlur = 8
  ctx.shadowOffsetY = 2
  ctx.beginPath()
  ctx.arc(center, center, radius, 0, Math.PI * 2)
  ctx.fillStyle = STATUS_BUCKET_COLOR[bucket]
  ctx.fill()
  ctx.restore()

  // 3. Ring stroke (theme-colored), 4px canvas = 2px logical.
  ctx.beginPath()
  ctx.arc(center, center, radius, 0, Math.PI * 2)
  ctx.lineWidth = 4
  ctx.strokeStyle = ringColor(theme)
  ctx.stroke()

  // 4. Icon: scale the 24x24 grid into a 0.52*S box, centered. After scaling by
  // (iconBox/24), lineWidth 2 renders as 2*(iconBox/24) canvas px — lucide's
  // native stroke-width kept proportional.
  const iconBox = 0.52 * size
  const scale = iconBox / 24
  ctx.save()
  ctx.translate(center - iconBox / 2, center - iconBox / 2)
  ctx.scale(scale, scale)
  ctx.strokeStyle = '#ffffff'
  ctx.lineWidth = 2
  ctx.lineCap = 'round'
  ctx.lineJoin = 'round'
  for (const d of ICON_PATHS[bucket]) {
    ctx.stroke(new Path2D(d))
  }
  ctx.restore()

  return ctx.getImageData(0, 0, size, size)
}

function addBadge(
  map: MapLibreMap,
  bucket: StatusBucket,
  important: boolean,
  theme: Theme,
): void {
  const name = markerImageName(bucket, important)
  if (map.hasImage(name)) return
  const logical = important ? LOGICAL_IMPORTANT : LOGICAL_BASE
  map.addImage(name, renderBadge(bucket, logical, theme), {
    pixelRatio: PIXEL_RATIO,
  })
}

/**
 * Ensures all 10 badge images exist on the map for `theme`, idempotently.
 * If the theme changed since last time, stale images are removed first so the
 * ring color regenerates. Call on map `load`.
 */
export function ensureMarkerImages(map: MapLibreMap, theme: Theme): void {
  const prev = themeForMap.get(map)
  if (prev != null && prev !== theme) {
    for (const bucket of BUCKETS) {
      for (const important of [false, true]) {
        const name = markerImageName(bucket, important)
        if (map.hasImage(name)) map.removeImage(name)
      }
    }
  }
  themeForMap.set(map, theme)
  for (const bucket of BUCKETS) {
    addBadge(map, bucket, false, theme)
    addBadge(map, bucket, true, theme)
  }
}

/**
 * Re-adds a single badge image requested via `styleimagemissing` — fired after
 * a basemap style swap (theme toggle) wipes every image. Ignores non-badge ids.
 */
export function addMissingMarkerImage(
  map: MapLibreMap,
  id: string,
  theme: Theme,
): void {
  const match =
    /^badge-(dispatch|ongoing|resolving|vigilancia|done)(-important)?$/.exec(id)
  if (!match) return
  themeForMap.set(map, theme)
  addBadge(map, match[1] as StatusBucket, match[2] != null, theme)
}
