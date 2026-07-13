// Native talks STRAIGHT to the public GraphQL endpoint: there is no CORS in a
// native runtime (the web app's server-fn indirection exists only for browser
// CORS + first-party key secrecy). Overridable via EXPO_PUBLIC_FOGOS_API_URL for
// local API work.

const DEFAULT_API_URL = 'https://api.fogosportugal.pt'

/** Absolute GraphQL endpoint URL for the Fogos API. */
export const FOGOS_ENDPOINT = `${(
  process.env.EXPO_PUBLIC_FOGOS_API_URL ?? DEFAULT_API_URL
).replace(/\/$/, '')}/graphql`

/** Public web app origin — the universal-link / share target. */
export const FOGOS_WEB_ORIGIN = 'https://fogosportugal.pt'

/**
 * Canonical shareable web URL for one incident. Matches the exact shape the web
 * app pushes (`/?incident=<id>`) and the app/universal links resolve — keep this
 * param name identical across web, deep links, and the AASA/assetlinks config.
 */
export function incidentWebUrl(id: string): string {
  return `${FOGOS_WEB_ORIGIN}/?incident=${encodeURIComponent(id)}`
}
