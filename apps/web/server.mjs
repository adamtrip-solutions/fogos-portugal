// Production Node host for the TanStack Start build.
//
// `vite build` emits a Web `fetch` handler at dist/server/server.js plus static client
// assets in dist/client — it does NOT emit a listening server. This wrapper mirrors what
// TanStack Start's own `vite preview` does: serve the built client files, and fall back to
// the SSR handler for everything else. srvx is a runtime dependency of @tanstack/react-start,
// so it is present in the pruned production node_modules.
import { serve } from 'srvx'
import { serveStatic } from 'srvx/static'
import handler from './dist/server/server.js'

const clientDir = new URL('./dist/client/', import.meta.url).pathname
const staticMiddleware = serveStatic({ dir: clientDir })

const port = Number(process.env.PORT ?? 3000)
const hostname = process.env.HOST ?? '0.0.0.0'

serve({
  port,
  hostname,
  // Static assets first (hashed JS/CSS, favicon, prerendered HTML); anything the client
  // build didn't produce falls through to the SSR fetch handler.
  fetch(request) {
    return staticMiddleware(request, () => handler.fetch(request))
  },
})

console.log(`web listening on http://${hostname}:${port}`)
