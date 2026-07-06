import { createStart } from '@tanstack/react-start'
import { clerkMiddleware } from '@clerk/tanstack-react-start/server'

// Global request middleware: Clerk resolves the browser session on every request
// and publishes `clerkInitialState` (incl. the publishable key) into the Start
// context, which `<ClerkProvider>` reads on the client — so BOTH keys stay
// server-side runtime env (CLERK_PUBLISHABLE_KEY / CLERK_SECRET_KEY), never a
// VITE_ build-time value baked into the prebuilt production image.
//
// With the keys unset the middleware degrades to a signed-out state (no throw,
// no redirect) and IsomorphicClerk never loads clerk.js — anonymous map/table
// flows are untouched. Accounts are simply "off" until the operator sets the
// two env vars (see deploy/compose.yml + ~/fogos-deploy/.env).
export const startInstance = createStart(() => ({
  requestMiddleware: [clerkMiddleware()],
}))
