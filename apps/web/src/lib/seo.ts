// Shared SEO helper. Routes spread `pageMeta(...)` into their `head()` return to
// get a consistent title, description, canonical URL and social-card tags.
//
// The root route (__root.tsx) defines the site-wide defaults (og:image, twitter:card,
// og:type, …). TanStack's head merge dedupes meta by `name`/`property`, so a per-route
// entry (e.g. og:title) overrides the root default while anything a route omits (e.g.
// og:image) falls back to the root value.

/** Canonical site origin. No trailing slash. */
export const SITE_ORIGIN = 'https://fogosportugal.pt'

/** Default social share image (1200×630). Served from /public. */
export const OG_DEFAULT_IMAGE = `${SITE_ORIGIN}/og-default.png`

export interface PageMetaInput {
  /** Full document title, including the " — FogosPortugal" suffix. */
  title: string
  /** Meta description (~150 chars, pt-PT). */
  description: string
  /** Absolute path starting with `/` — used to build the canonical + og:url. */
  path: string
  /** Absolute image URL to override the root og:image / twitter:image. */
  image?: string
  /** When true, emit `robots: noindex, nofollow` (private or low-value pages). */
  noindex?: boolean
}

export interface SeoTags {
  meta: Array<Record<string, string>>
  links: Array<Record<string, string>>
}

/** Build the meta + links arrays for a route's `head()`. */
export function pageMeta({
  title,
  description,
  path,
  image,
  noindex,
}: PageMetaInput): SeoTags {
  const url = `${SITE_ORIGIN}${path}`

  const meta: Array<Record<string, string>> = [
    { title },
    { name: 'description', content: description },
    { property: 'og:title', content: title },
    { property: 'og:description', content: description },
    { property: 'og:url', content: url },
    { name: 'twitter:title', content: title },
    { name: 'twitter:description', content: description },
  ]

  if (image) {
    meta.push(
      { property: 'og:image', content: image },
      { name: 'twitter:image', content: image },
    )
  }

  if (noindex) {
    meta.push({ name: 'robots', content: 'noindex, nofollow' })
  }

  return {
    meta,
    links: [{ rel: 'canonical', href: url }],
  }
}

/**
 * Wrap a JSON-LD object as a meta entry. TanStack serializes the special
 * `script:ld+json` key into an SSR'd `<script type="application/ld+json">`.
 * The head meta type only models real <meta> attributes, so the shape is cast
 * to blend into a route's meta array.
 */
export function ldJson(data: Record<string, unknown>): Record<string, string> {
  return { 'script:ld+json': data } as unknown as Record<string, string>
}
