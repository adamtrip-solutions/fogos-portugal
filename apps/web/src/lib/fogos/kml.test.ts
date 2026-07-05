// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'

import { parseKmlToGeoJson } from './kml.ts'

const POLYGON_KML = `<?xml version="1.0" encoding="UTF-8"?>
<kml xmlns="http://www.opengis.net/kml/2.2">
  <Document>
    <Placemark>
      <name>Perímetro</name>
      <Polygon>
        <outerBoundaryIs>
          <LinearRing>
            <coordinates>
              -8.0,40.0,0 -8.1,40.0,0 -8.1,40.1,0 -8.0,40.1,0 -8.0,40.0,0
            </coordinates>
          </LinearRing>
        </outerBoundaryIs>
      </Polygon>
    </Placemark>
  </Document>
</kml>`

describe('parseKmlToGeoJson', () => {
  it('converts a KML polygon into a GeoJSON FeatureCollection', () => {
    const fc = parseKmlToGeoJson(POLYGON_KML)
    expect(fc.type).toBe('FeatureCollection')
    expect(fc.features).toHaveLength(1)
    const geometry = fc.features[0].geometry
    expect(geometry?.type).toBe('Polygon')
  })

  it('preserves the ring coordinates in [lng, lat] order', () => {
    const fc = parseKmlToGeoJson(POLYGON_KML)
    const geometry = fc.features[0].geometry
    if (geometry?.type !== 'Polygon') throw new Error('expected a Polygon')
    // togeojson keeps the KML altitude as a third ordinate.
    const first = geometry.coordinates[0][0]
    expect([first[0], first[1]]).toEqual([-8, 40])
  })

  it('returns an empty collection for empty input', () => {
    expect(parseKmlToGeoJson('')).toEqual({
      type: 'FeatureCollection',
      features: [],
    })
  })

  it('returns an empty collection for a document with no placemarks', () => {
    const fc = parseKmlToGeoJson(
      '<kml xmlns="http://www.opengis.net/kml/2.2"><Document/></kml>',
    )
    expect(fc.features).toHaveLength(0)
  })
})
