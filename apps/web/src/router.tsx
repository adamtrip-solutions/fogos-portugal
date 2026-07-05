import {
  createRouter as createTanStackRouter,
  stringifySearchWith,
} from '@tanstack/react-router'
import { routeTree } from './routeTree.gen'

import { setupRouterSsrQueryIntegration } from '@tanstack/react-router-ssr-query'
import { getContext } from './integrations/tanstack-query/root-provider'

export function getRouter() {
  const context = getContext()

  const router = createTanStackRouter({
    routeTree,
    context,
    scrollRestoration: true,
    defaultPreload: 'intent',
    defaultPreloadStaleTime: 0,
    // Serialize search values as plain strings. The default stringifier passes a
    // `JSON.parse` probe that re-quotes any string that *looks* like JSON — so a
    // numeric fire id (`?incident=2026070400004`) would round-trip to the quoted
    // form `?incident="2026070400004"` and SSR would 307-redirect to it, dropping
    // the deep-link before hydration. Omitting the probe keeps ids unquoted and
    // stable, so shared links SSR with the param intact (200, no redirect).
    stringifySearch: stringifySearchWith(JSON.stringify),
    defaultNotFoundComponent: () => (
      <main className="flex h-[100dvh] w-full flex-col items-center justify-center gap-2">
        <h1 className="text-2xl font-semibold">Página não encontrada</h1>
        <a href="/" className="text-sm text-muted-foreground underline">
          Voltar ao mapa
        </a>
      </main>
    ),
  })

  setupRouterSsrQueryIntegration({ router, queryClient: context.queryClient })

  return router
}

declare module '@tanstack/react-router' {
  interface Register {
    router: ReturnType<typeof getRouter>
  }
}
