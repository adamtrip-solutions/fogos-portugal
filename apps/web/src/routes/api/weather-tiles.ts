import { createFileRoute } from '@tanstack/react-router'

import { ALL_WMS_LAYERS } from '#/lib/weather/catalog.ts'

// Server-side proxy for IPMA WMS tiles: IPMA sends no CORS headers, so
// MapLibre cannot fetch tiles directly. MapLibre substitutes the per-tile
// `{bbox-epsg-3857}` token into `bbox`, and this handler forwards a GetMap to
// IPMA with fixed WMS parameters. Only allowlisted layer names are proxied.

const IPMA_WMS = 'https://mf2.ipma.pt/services/'
const TIME_RE = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/

function badRequest(message: string): Response {
  return new Response(message, {
    status: 400,
    headers: { 'content-type': 'text/plain; charset=utf-8' },
  })
}

export const Route = createFileRoute('/api/weather-tiles')({
  server: {
    handlers: {
      GET: async ({ request }) => {
        const params = new URL(request.url).searchParams

        const layersParam = params.get('layers')
        if (!layersParam) return badRequest('Missing "layers"')
        const layers = layersParam.split(',')
        if (layers.some((layer) => !ALL_WMS_LAYERS.has(layer))) {
          return badRequest('Invalid "layers"')
        }

        const bboxParam = params.get('bbox')
        if (!bboxParam) return badRequest('Missing "bbox"')
        const bboxParts = bboxParam.split(',')
        if (
          bboxParts.length !== 4 ||
          bboxParts.some((n) => !Number.isFinite(Number(n)))
        ) {
          return badRequest('Invalid "bbox"')
        }

        const time = params.get('time')
        const referenceTime = params.get('reference_time')
        if (time != null && !TIME_RE.test(time)) return badRequest('Invalid "time"')
        if (referenceTime != null && !TIME_RE.test(referenceTime)) {
          return badRequest('Invalid "reference_time"')
        }

        const upstream = new URLSearchParams({
          service: 'WMS',
          version: '1.3.0',
          request: 'GetMap',
          styles: '',
          format: 'image/png',
          transparent: 'true',
          crs: 'EPSG:3857',
          width: '256',
          height: '256',
          layers: layers.join(','),
          bbox: bboxParam,
        })
        if (time != null) upstream.set('time', time)
        if (referenceTime != null) upstream.set('reference_time', referenceTime)

        let res: Response
        try {
          res = await fetch(`${IPMA_WMS}?${upstream.toString()}`)
        } catch {
          // Upstream unreachable — 204 keeps MapLibre from spamming errors.
          return new Response(null, { status: 204 })
        }

        const contentType = res.headers.get('content-type') ?? ''
        if (!res.ok || !contentType.includes('image/png')) {
          // IPMA answers 404 with an HTML body for unavailable runs/domains.
          return new Response(null, { status: 204 })
        }

        return new Response(await res.arrayBuffer(), {
          status: 200,
          headers: {
            'content-type': 'image/png',
            'cache-control': 'public, max-age=900',
          },
        })
      },
    },
  },
})
