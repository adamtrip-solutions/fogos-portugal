import {
  HeadContent,
  Scripts,
  createRootRouteWithContext,
} from '@tanstack/react-router'
import { TanStackRouterDevtoolsPanel } from '@tanstack/react-router-devtools'
import { TanStackDevtools } from '@tanstack/react-devtools'

import TanStackQueryDevtools from '../integrations/tanstack-query/devtools'

import { THEME_INIT_SCRIPT } from '#/lib/theme.ts'
import { ToastProvider } from '#/components/toast.tsx'

import 'maplibre-gl/dist/maplibre-gl.css'
import appCss from '../styles.css?url'

import type { QueryClient } from '@tanstack/react-query'

interface MyRouterContext {
  queryClient: QueryClient
}

export const Route = createRootRouteWithContext<MyRouterContext>()({
  head: () => ({
    meta: [
      {
        charSet: 'utf-8',
      },
      {
        name: 'viewport',
        content: 'width=device-width, initial-scale=1',
      },
      {
        title: 'FogosPortugal — Incêndios em Portugal',
      },
      {
        name: 'description',
        content: 'Mapa em tempo real dos incêndios em Portugal.',
      },
      { property: 'og:type', content: 'website' },
      { property: 'og:site_name', content: 'FogosPortugal' },
      { property: 'og:title', content: 'FogosPortugal — Incêndios em Portugal' },
      {
        property: 'og:description',
        content: 'Mapa em tempo real dos incêndios em Portugal.',
      },
      { property: 'og:url', content: 'https://fogosportugal.pt' },
      { name: 'twitter:card', content: 'summary_large_image' },
      { name: 'twitter:title', content: 'FogosPortugal — Incêndios em Portugal' },
      {
        name: 'twitter:description',
        content: 'Mapa em tempo real dos incêndios em Portugal.',
      },
    ],
    links: [
      {
        rel: 'stylesheet',
        href: appCss,
      },
    ],
  }),
  shellComponent: RootDocument,
})

function RootDocument({ children }: { children: React.ReactNode }) {
  return (
    // suppressHydrationWarning: the theme init script sets `class="dark"` before hydration.
    <html lang="pt" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: THEME_INIT_SCRIPT }} />
        <HeadContent />
      </head>
      <body>
        <ToastProvider>{children}</ToastProvider>
        <TanStackDevtools
          config={{
            position: 'bottom-right',
          }}
          plugins={[
            {
              name: 'Tanstack Router',
              render: <TanStackRouterDevtoolsPanel />,
            },
            TanStackQueryDevtools,
          ]}
        />
        <Scripts />
      </body>
    </html>
  )
}
