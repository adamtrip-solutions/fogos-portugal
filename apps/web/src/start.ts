import { createStart } from '@tanstack/react-start'
import { clerkMiddleware } from '@clerk/tanstack-react-start/server'

// Accounts are ON only when the operator sets CLERK_SECRET_KEY (runtime env —
// see deploy/compose.yml; both Clerk keys default to empty = off).
//
// The middleware MUST NOT be registered when the key is missing: with
// NODE_ENV=production and no secret key, Clerk's loadOptions throws ("no secret
// key provided") on EVERY SSR request — keyless fallback is dev-only — which
// would 500 the whole site under the default deploy. `typeof process` guard:
// this file is part of the isomorphic Start entry, and the browser bundle has
// no `process` global.
const clerkEnabled =
  typeof process !== 'undefined' && !!process.env.CLERK_SECRET_KEY

// When enabled, Clerk resolves the browser session on every request and
// publishes `clerkInitialState` (incl. the publishable key) into the Start
// context, which `<ClerkProvider>` reads on the client — so BOTH keys stay
// server-side runtime env (CLERK_PUBLISHABLE_KEY / CLERK_SECRET_KEY), never a
// VITE_ build-time value baked into the prebuilt production image.
//
// When disabled, ClerkProvider stays inert (no publishable key ⇒ clerk.js never
// loads, `useAuth().isLoaded` never turns true) and /conta shows its
// "accounts unavailable" card via the accountsEnabled server fn — anonymous
// map/table flows are untouched either way.
export const startInstance = createStart(() => ({
  requestMiddleware: clerkEnabled ? [clerkMiddleware()] : [],
}))
