import { createFileRoute } from '@tanstack/react-router'

import { CONCELHOS } from '#/lib/fogos/concelhos.ts'
import { SITE_ORIGIN } from '#/lib/seo.ts'

// Dynamically generated sitemap. Filename `sitemap[.]xml.ts` maps to `/sitemap.xml`
// (the `[.]` escapes the literal dot in the URL segment). Private/low-value routes
// (/conta) are intentionally excluded.

interface SitemapEntry {
  path: string
  changefreq: 'hourly' | 'daily' | 'weekly'
  priority: string
}

// Public, indexable static routes that exist on this base.
const STATIC_ENTRIES: Array<SitemapEntry> = [
  { path: '/', changefreq: 'hourly', priority: '1.0' },
  { path: '/situacao', changefreq: 'hourly', priority: '0.9' },
  { path: '/ocorrencias', changefreq: 'daily', priority: '0.9' },
  { path: '/avisos', changefreq: 'daily', priority: '0.9' },
  { path: '/estatisticas', changefreq: 'daily', priority: '0.9' },
  { path: '/sobre', changefreq: 'daily', priority: '0.5' },
  { path: '/creditos', changefreq: 'daily', priority: '0.5' },
  { path: '/api', changefreq: 'daily', priority: '0.5' },
]

function urlEntry({ path, changefreq, priority }: SitemapEntry): string {
  return (
    `  <url>\n` +
    `    <loc>${SITE_ORIGIN}${path}</loc>\n` +
    `    <changefreq>${changefreq}</changefreq>\n` +
    `    <priority>${priority}</priority>\n` +
    `  </url>`
  )
}

function buildSitemap(): string {
  const entries: Array<SitemapEntry> = [
    ...STATIC_ENTRIES,
    ...CONCELHOS.map(
      (c): SitemapEntry => ({
        path: `/concelho/${c.dico}`,
        changefreq: 'daily',
        priority: '0.6',
      }),
    ),
  ]

  return (
    `<?xml version="1.0" encoding="UTF-8"?>\n` +
    `<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n` +
    entries.map(urlEntry).join('\n') +
    `\n</urlset>\n`
  )
}

export const Route = createFileRoute('/sitemap.xml')({
  server: {
    handlers: {
      GET: () =>
        new Response(buildSitemap(), {
          status: 200,
          headers: {
            'content-type': 'application/xml; charset=utf-8',
            'cache-control': 'public, max-age=3600',
          },
        }),
    },
  },
})
