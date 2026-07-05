import { kml } from '@tmcw/togeojson'

import type { Feature, FeatureCollection, Geometry } from 'geojson'

const EMPTY: FeatureCollection<Geometry> = {
  type: 'FeatureCollection',
  features: [],
}

function hasGeometry(feature: Feature<Geometry | null>): feature is Feature<Geometry> {
  return feature.geometry != null
}

/**
 * Parse a raw KML perimeter document into GeoJSON, dropping placemarks with no
 * geometry (map layers only render concrete geometries). Client-only: relies on
 * the browser `DOMParser` (the map is client-rendered, and KML is fetched lazily
 * after selection). Returns an empty collection on a parse failure or when the
 * environment lacks `DOMParser` (e.g. SSR) rather than throwing.
 */
export function parseKmlToGeoJson(kmlText: string): FeatureCollection<Geometry> {
  if (typeof DOMParser === 'undefined' || !kmlText) return EMPTY
  try {
    const doc = new DOMParser().parseFromString(kmlText, 'text/xml')
    // A malformed document yields a <parsererror> node; togeojson then finds
    // no placemarks and returns an empty collection, which is the desired
    // graceful outcome.
    const fc = kml(doc)
    return {
      type: 'FeatureCollection',
      features: fc.features.filter(hasGeometry),
    }
  } catch {
    return EMPTY
  }
}
