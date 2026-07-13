/**
 * Error raised by the Fogos GraphQL client. Carries the API's error `code` (from
 * a GraphQL `extensions.code` or an auth-middleware error body) plus the HTTP
 * `status` when the failure was transport-level, so callers can map it to copy
 * or to recovery logic (e.g. a `DEVICE_UNAUTHENTICATED` 401 → re-register).
 */
export class FogosApiError extends Error {
  readonly code: string | undefined
  readonly status: number | undefined

  constructor(
    message: string,
    options?: { code?: string | undefined; status?: number | undefined },
  ) {
    super(message)
    this.name = 'FogosApiError'
    this.code = options?.code
    this.status = options?.status
  }
}
