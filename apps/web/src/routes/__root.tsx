import {
  HeadContent,
  Scripts,
  createRootRouteWithContext,
} from '@tanstack/react-router'
import { TanStackRouterDevtoolsPanel } from '@tanstack/react-router-devtools'
import { TanStackDevtools } from '@tanstack/react-devtools'
import { ClerkProvider } from '@clerk/tanstack-react-start'

import TanStackQueryDevtools from '../integrations/tanstack-query/devtools'

import { THEME_INIT_SCRIPT } from '#/lib/theme.ts'
import { OG_DEFAULT_IMAGE, SITE_ORIGIN, ldJson } from '#/lib/seo.ts'

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
      { property: 'og:image', content: OG_DEFAULT_IMAGE },
      { property: 'og:image:width', content: '1200' },
      { property: 'og:image:height', content: '630' },
      { name: 'twitter:card', content: 'summary_large_image' },
      { name: 'twitter:title', content: 'FogosPortugal — Incêndios em Portugal' },
      {
        name: 'twitter:description',
        content: 'Mapa em tempo real dos incêndios em Portugal.',
      },
      { name: 'twitter:image', content: OG_DEFAULT_IMAGE },
      // Structured data: identifies the site + publisher to search engines.
      // TanStack serializes `script:ld+json` into an SSR'd <script type="application/ld+json">.
      ldJson({
        '@context': 'https://schema.org',
        '@type': 'WebSite',
        name: 'FogosPortugal',
        url: SITE_ORIGIN,
        inLanguage: 'pt-PT',
      }),
      ldJson({
        '@context': 'https://schema.org',
        '@type': 'Organization',
        name: 'FogosPortugal',
        url: SITE_ORIGIN,
        logo: `${SITE_ORIGIN}/icon-512.png`,
      }),
    ],
    links: [
      {
        rel: 'stylesheet',
        href: appCss,
      },
      { rel: 'icon', href: '/favicon.ico', sizes: '48x48' },
      {
        rel: 'icon',
        type: 'image/png',
        sizes: '16x16',
        href: '/favicon-16.png',
      },
      {
        rel: 'icon',
        type: 'image/png',
        sizes: '32x32',
        href: '/favicon-32.png',
      },
      { rel: 'apple-touch-icon', sizes: '180x180', href: '/apple-touch-icon.png' },
      { rel: 'manifest', href: '/manifest.json' },
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
        {/* Clerk publishes its state via the global clerkMiddleware (src/start.ts);
            ClerkProvider reads it here so the publishable key stays server-runtime,
            never a VITE_ build value. With keys unset it stays inert (signed-out). */}
        <ClerkProvider>{children}</ClerkProvider>
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
