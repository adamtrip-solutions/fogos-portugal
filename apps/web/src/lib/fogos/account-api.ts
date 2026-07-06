import { createServerFn } from '@tanstack/react-start'
import { auth } from '@clerk/tanstack-react-start/server'
import { queryOptions } from '@tanstack/react-query'

// ── Header-choice rule ───────────────────────────────────────────────────────
//
// `api.ts` (X-API-Key, first-party) = PUBLIC data, served the same to everyone.
// `account-api.ts` (Bearer, Clerk) = the CALLER's OWN data (keys, subscriptions,
// webhooks). Every call here attaches a freshly-minted Clerk session token — they
// live ~60s, so we never cache one — and NEVER the X-API-Key. The API routes the
// Bearer by its `iss` to the Clerk validator, provisions the user, and answers as
// the Registered-tier caller that owns the data.
//
// Runs only inside createServerFn handlers: `auth()` reads the Clerk session that
// the global clerkMiddleware (src/start.ts) resolved for this request.

interface GraphQLResponse<T> {
  data?: T
  errors?: Array<{ message: string; extensions?: { code?: string } }>
}

/** Thrown with the API's GraphQL error code so the UI can map it to pt-PT copy. */
export class AccountApiError extends Error {
  readonly code?: string
  constructor(message: string, code?: string) {
    super(message)
    this.name = 'AccountApiError'
    this.code = code
  }
}

async function graphqlAsUser<T>(
  query: string,
  variables?: Record<string, unknown>,
): Promise<T> {
  const { getToken } = await auth()
  const token = await getToken()
  if (!token) {
    throw new AccountApiError('Sessão não autenticada.', 'UNAUTHENTICATED')
  }

  const endpoint = `${process.env.FOGOS_API_URL ?? 'http://localhost:5077'}/graphql`
  const res = await fetch(endpoint, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ query, variables }),
  })

  if (!res.ok) {
    throw new AccountApiError(`Fogos API respondeu com ${res.status}`)
  }

  const json = (await res.json()) as GraphQLResponse<T>
  if (json.errors && json.errors.length > 0) {
    const first = json.errors[0]
    throw new AccountApiError(
      json.errors.map((e) => e.message).join('; '),
      first.extensions?.code,
    )
  }
  if (!json.data) {
    throw new AccountApiError('Fogos API não devolveu dados.')
  }
  return json.data
}

// ── Types (mirror the API's PR2 GraphQL shapes) ──────────────────────────────

export type UserRole = 'USER' | 'ADMIN'

export interface ApiKeyInfo {
  id: string
  name: string | null
  /** First 12 chars of the key, display-only (e.g. `fpk_a1b2c3d4`). */
  keyPrefix: string | null
  createdAt: string
  /** Non-null ⇒ the key is revoked. */
  revokedAt: string | null
}

/** Show-once result of `createApiKey`: the plaintext is never returned again. */
export interface CreatedApiKey {
  apiKey: ApiKeyInfo
  plaintextKey: string
}

export interface Webhook {
  id: string
  url: string
  events: string[]
  active: boolean
  consecutiveFailures: number
  createdAt: string
}

export type AlertKind = 'CONCELHO' | 'POINT'

export interface OwnedAlertSubscription {
  id: string
  kind: AlertKind
  dico: string | null
  point: { latitude: number; longitude: number } | null
  radiusKm: number | null
  riskThreshold: number | null
  createdAt: string
  lastSeenAt: string | null
}

export interface Me {
  id: string
  email: string | null
  name: string | null
  role: UserRole
  apiKeys: ApiKeyInfo[]
  webhooks: Webhook[]
  alertSubscriptions: OwnedAlertSubscription[]
}

/** Input for create/update alert subscription (update re-runs create validation). */
export interface AlertSubscriptionInput {
  kind: AlertKind
  dico?: string | null
  latitude?: number | null
  longitude?: number | null
  radiusKm?: number | null
  riskThreshold?: number | null
}

// ── Accounts feature flag ────────────────────────────────────────────────────

// Whether the accounts feature is on for this deployment: clerkMiddleware is
// only registered when CLERK_SECRET_KEY is set (src/start.ts), and with the
// middleware absent ClerkProvider never reaches `isLoaded` — so /conta gates on
// this server fn instead of Clerk state to avoid an infinite spinner.
export const fetchAccountsEnabled = createServerFn({ method: 'GET' }).handler(
  async () => !!process.env.CLERK_SECRET_KEY,
)

export const accountsEnabledQuery = () =>
  queryOptions({
    queryKey: ['accounts-enabled'] as const,
    queryFn: () => fetchAccountsEnabled(),
    // Fixed for the lifetime of the server process.
    staleTime: Number.POSITIVE_INFINITY,
    gcTime: Number.POSITIVE_INFINITY,
  })

// ── Queries ──────────────────────────────────────────────────────────────────

const API_KEY_FIELDS = /* GraphQL */ `
  id
  name
  keyPrefix
  createdAt
  revokedAt
`

const ALERT_SUB_FIELDS = /* GraphQL */ `
  id
  kind
  dico
  point { latitude longitude }
  radiusKm
  riskThreshold
  createdAt
  lastSeenAt
`

const ME_QUERY = /* GraphQL */ `
  query Me {
    me {
      id
      email
      name
      role
      apiKeys { ${API_KEY_FIELDS} }
      webhooks { id url events active consecutiveFailures createdAt }
      alertSubscriptions { ${ALERT_SUB_FIELDS} }
    }
  }
`

export const fetchMe = createServerFn({ method: 'GET' }).handler(async () => {
  const data = await graphqlAsUser<{ me: Me | null }>(ME_QUERY)
  return data.me
})

export const meQuery = () =>
  queryOptions({
    queryKey: ['me'] as const,
    queryFn: () => fetchMe(),
    staleTime: 30_000,
  })

// ── Mutations ────────────────────────────────────────────────────────────────

const CREATE_API_KEY_MUTATION = /* GraphQL */ `
  mutation CreateApiKey($name: String!) {
    createApiKey(name: $name) {
      plaintextKey
      apiKey { ${API_KEY_FIELDS} }
    }
  }
`

export const createApiKey = createServerFn({ method: 'POST' })
  .validator((name: string) => name)
  .handler(async ({ data: name }): Promise<CreatedApiKey> => {
    const data = await graphqlAsUser<{ createApiKey: CreatedApiKey }>(
      CREATE_API_KEY_MUTATION,
      { name },
    )
    return data.createApiKey
  })

const REVOKE_API_KEY_MUTATION = /* GraphQL */ `
  mutation RevokeApiKey($id: ID!) {
    revokeApiKey(id: $id)
  }
`

export const revokeApiKey = createServerFn({ method: 'POST' })
  .validator((id: string) => id)
  .handler(async ({ data: id }): Promise<boolean> => {
    const data = await graphqlAsUser<{ revokeApiKey: boolean }>(
      REVOKE_API_KEY_MUTATION,
      { id },
    )
    return data.revokeApiKey
  })

const CREATE_ALERT_SUB_MUTATION = /* GraphQL */ `
  mutation CreateAlertSubscription($input: CreateAlertSubscriptionInput!) {
    createAlertSubscription(input: $input) { ${ALERT_SUB_FIELDS} }
  }
`

export const createAlertSubscription = createServerFn({ method: 'POST' })
  .validator((input: AlertSubscriptionInput) => input)
  .handler(async ({ data: input }): Promise<OwnedAlertSubscription> => {
    const data = await graphqlAsUser<{
      createAlertSubscription: OwnedAlertSubscription
    }>(CREATE_ALERT_SUB_MUTATION, { input })
    return data.createAlertSubscription
  })

const UPDATE_ALERT_SUB_MUTATION = /* GraphQL */ `
  mutation UpdateAlertSubscription($id: ID!, $input: CreateAlertSubscriptionInput!) {
    updateAlertSubscription(id: $id, input: $input) { ${ALERT_SUB_FIELDS} }
  }
`

export const updateAlertSubscription = createServerFn({ method: 'POST' })
  .validator((data: { id: string; input: AlertSubscriptionInput }) => data)
  .handler(async ({ data }): Promise<OwnedAlertSubscription> => {
    const result = await graphqlAsUser<{
      updateAlertSubscription: OwnedAlertSubscription
    }>(UPDATE_ALERT_SUB_MUTATION, { id: data.id, input: data.input })
    return result.updateAlertSubscription
  })

const DELETE_ALERT_SUB_MUTATION = /* GraphQL */ `
  mutation DeleteAlertSubscription($id: ID!) {
    deleteAlertSubscription(id: $id)
  }
`

export const deleteAlertSubscription = createServerFn({ method: 'POST' })
  .validator((id: string) => id)
  .handler(async ({ data: id }): Promise<boolean> => {
    const data = await graphqlAsUser<{ deleteAlertSubscription: boolean }>(
      DELETE_ALERT_SUB_MUTATION,
      { id },
    )
    return data.deleteAlertSubscription
  })
