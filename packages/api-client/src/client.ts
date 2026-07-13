// Transport-agnostic GraphQL client for the Fogos API. Pure `fetch` — no React,
// RN, or codegen. Runs natively (mobile talks straight to the public endpoint;
// no CORS in a native runtime) and inside the web server functions later.

import { FogosApiError } from './errors'

interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string; extensions?: { code?: string } }>
}

/** Auth-middleware error body shape (`{ error, message }`) returned on a hard 401/403. */
interface HttpErrorBody {
  error?: string
  message?: string
}

export interface FogosClient {
  /**
   * Executes a GraphQL operation. Throws {@link FogosApiError} on transport
   * failure (non-2xx, carrying `status` and any middleware error `code`),
   * GraphQL errors (joined message + first `extensions.code`), or a missing
   * `data` payload.
   */
  graphql<T>(
    query: string,
    variables?: Record<string, unknown>,
    signal?: AbortSignal,
  ): Promise<T>
}

export interface CreateFogosClientOptions {
  /** Absolute GraphQL endpoint URL, e.g. `https://api.fogosportugal.pt/graphql`. */
  endpoint: string
  /** Optional per-request header provider (e.g. a device-key auth header). */
  getHeaders?: () =>
    | Promise<Record<string, string>>
    | Record<string, string>
}

export function createFogosClient(options: CreateFogosClientOptions): FogosClient {
  const { endpoint, getHeaders } = options

  return {
    async graphql<T>(
      query: string,
      variables?: Record<string, unknown>,
      signal?: AbortSignal,
    ): Promise<T> {
      const extraHeaders = getHeaders ? await getHeaders() : undefined

      const res = await fetch(endpoint, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...extraHeaders },
        body: JSON.stringify({ query, variables }),
        signal,
      })

      if (!res.ok) {
        // Auth middleware returns `{ error, message }` on a hard 401/403 before
        // GraphQL runs; surface its code so callers can recover (e.g. re-register
        // on DEVICE_UNAUTHENTICATED). Fall back to a status message otherwise.
        let code: string | undefined
        try {
          const body = (await res.json()) as HttpErrorBody
          if (typeof body?.error === 'string') code = body.error
        } catch {
          // non-JSON body — status is enough
        }
        throw new FogosApiError(`Fogos API responded with ${res.status}`, {
          code,
          status: res.status,
        })
      }

      const json = (await res.json()) as GraphQLResponse<T>

      if (json.errors && json.errors.length > 0) {
        const first = json.errors[0]
        throw new FogosApiError(json.errors.map((e) => e.message).join('; '), {
          code: first?.extensions?.code,
        })
      }

      if (!json.data) {
        throw new FogosApiError('Fogos API returned no data')
      }

      return json.data
    },
  }
}
