/**
 * Search-param helpers for the `/` route's `?incident=<id>` deep link.
 *
 * Fire ids are numeric strings (e.g. `2026070400004`). TanStack Router's default
 * search parser JSON-decodes each value, so `?incident=2026070400004` arrives as
 * a `number`, not a string. Normalising here (number → string) keeps the id a
 * string everywhere downstream and — together with the router's plain-string
 * search serializer (see `router.tsx`) — lets shared links SSR without the param
 * being dropped by a 307 redirect.
 */
export interface IndexSearch {
  incident?: string
}

/** Coerce a raw parsed search value into a non-empty incident id, or undefined. */
export function normalizeIncidentParam(raw: unknown): string | undefined {
  const value =
    typeof raw === 'string'
      ? raw
      : typeof raw === 'number' && Number.isFinite(raw)
        ? String(raw)
        : undefined
  return value && value.length > 0 ? value : undefined
}
