// Minimal fetch-based GraphQL client for the Fogos API — ported from
// apps/web/src/lib/fogos/api.ts, stripped of the TanStack server-fn wrappers.
//
// Native talks STRAIGHT to the public GraphQL endpoint: there is no CORS in a
// native runtime (the web app's server-fn indirection exists only for browser
// CORS + first-party key secrecy). No API key is embedded — anything shipped in
// an app binary is extractable, and the anonymous per-IP tier (30/min) is
// naturally per-user on mobile networks. See the mobile plan.
//
// Endpoint is overridable via EXPO_PUBLIC_FOGOS_API_URL for local API work.

import type { IncidentListItem } from './types'

const DEFAULT_API_URL = 'https://api.fogosportugal.pt'

function endpoint(): string {
  const base = process.env.EXPO_PUBLIC_FOGOS_API_URL ?? DEFAULT_API_URL
  return `${base.replace(/\/$/, '')}/graphql`
}

// Same field set the web map's list uses (activeIncidents → IncidentListItem).
const ACTIVE_INCIDENTS_QUERY = /* GraphQL */ `
  query Active {
    activeIncidents {
      id
      location
      district
      concelho
      freguesia
      coordinates { latitude longitude }
      status { code label color }
      kind
      natureza
      important
      occurredAt
      updatedAt
      statusChangedAt
      resources { man terrain aerial aquatic }
      signals { escalating rekindle criticalConditions }
    }
  }
`

interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string; extensions?: { code?: string } }>
}

/** GraphQL error carrying the API's error `code` so callers can map it to copy. */
export class FogosApiError extends Error {
  readonly code?: string
  constructor(message: string, code?: string) {
    super(message)
    this.name = 'FogosApiError'
    this.code = code
  }
}

async function graphql<T>(
  query: string,
  variables?: Record<string, unknown>,
  signal?: AbortSignal,
): Promise<T> {
  const res = await fetch(endpoint(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ query, variables }),
    signal,
  })

  if (!res.ok) {
    throw new FogosApiError(`Fogos API responded with ${res.status}`)
  }

  const json = (await res.json()) as GraphQLResponse<T>

  if (json.errors && json.errors.length > 0) {
    const first = json.errors[0]
    throw new FogosApiError(
      json.errors.map((e) => e.message).join('; '),
      first.extensions?.code,
    )
  }

  if (!json.data) {
    throw new FogosApiError('Fogos API returned no data')
  }

  return json.data
}

/** The live feed of active/ongoing incidents (status codes 3–9) for the map. */
export async function fetchActiveIncidents(
  signal?: AbortSignal,
): Promise<IncidentListItem[]> {
  const data = await graphql<{ activeIncidents: IncidentListItem[] }>(
    ACTIVE_INCIDENTS_QUERY,
    undefined,
    signal,
  )
  return data.activeIncidents
}
