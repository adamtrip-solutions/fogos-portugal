import { useEffect } from 'react'
import { useControl } from 'react-map-gl/maplibre'
import { MapboxOverlay } from '@deck.gl/mapbox'
import { ImageType, ParticleLayer } from 'weatherlayers-gl'
import type { TextureData } from 'weatherlayers-gl'

import type { Theme } from '#/lib/theme.ts'
import type { WindField } from '#/lib/weather/wind.ts'

// Animated wind particles (Windy-style), drawn over the IPMA windintensity
// raster. deck.gl's MapboxOverlay shares MapLibre's WebGL2 context in
// interleaved mode; weatherlayers-gl's ParticleLayer advects particles along a
// u/v vector field. This whole module (deck.gl + luma.gl + weatherlayers-gl) is
// heavy and WebGL-only, so it is loaded lazily and never evaluated during SSR.

// Particle count per field — order matches the grids in `wind.ts`
// (continent, madeira, azores). Dense continent, sparse islands.
const NUM_PARTICLES = [4000, 800, 2000]

// Particle colour by basemap: near-black on the light (positron) basemap,
// near-white on the dark (dark-matter) basemap.
const LIGHT_COLOR: [number, number, number] = [39, 39, 42]
const DARK_COLOR: [number, number, number] = [235, 235, 240]

/** Packs a wind field into an interleaved 2-channel float image (u, v). */
function toTextureData(field: WindField): TextureData {
  const { width, height, u, v } = field
  const data = new Float32Array(width * height * 2)
  for (let i = 0; i < width * height; i++) {
    data[i * 2] = u[i]
    data[i * 2 + 1] = v[i]
  }
  return { data, width, height }
}

function buildLayers(fields: WindField[], theme: Theme): ParticleLayer[] {
  const color = theme === 'dark' ? DARK_COLOR : LIGHT_COLOR
  return fields.map(
    (field, i) =>
      new ParticleLayer({
        id: `wind-particles-${i}`,
        image: toTextureData(field),
        imageType: ImageType.VECTOR,
        // Float image already holds m/s components, so no unscaling.
        imageUnscale: null,
        bounds: field.bounds,
        numParticles: NUM_PARTICLES[i] ?? 2000,
        maxAge: 30,
        speedFactor: 2,
        width: 1.5,
        color,
        opacity: 0.9,
        // deck.gl 9 typings expose no per-layer `beforeId` for interleaved
        // MapboxOverlay, so particles ride on top; never intercept clicks.
        pickable: false,
      }),
  )
}

interface WindParticlesProps {
  fields: WindField[]
  theme: Theme
}

export function WindParticles({ fields, theme }: WindParticlesProps) {
  const overlay = useControl(
    () => new MapboxOverlay({ interleaved: true, layers: [] }),
  )

  useEffect(() => {
    overlay.setProps({ layers: buildLayers(fields, theme) })
  }, [overlay, fields, theme])

  return null
}

export default WindParticles
