// TanStack Query wiring for RN (plan F1). The QueryClient + AsyncStorage
// persister live here so the cache survives a cold, offline launch (emergency
// app: last-known fires must be visible with no network). `onlineManager` is
// wired to NetInfo here at module load; `focusManager` ↔ AppState is wired in
// the root layout (it needs component-lifecycle cleanup).

import AsyncStorage from '@react-native-async-storage/async-storage'
import NetInfo from '@react-native-community/netinfo'
import {
  QueryClient,
  defaultShouldDehydrateQuery,
  onlineManager,
  type Query,
} from '@tanstack/react-query'
import { createAsyncStoragePersister } from '@tanstack/query-async-storage-persister'

/** Keep persisted queries in memory for the full persist window (24h). */
export const CACHE_MAX_AGE = 1000 * 60 * 60 * 24

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 55_000,
      // gcTime must be >= the persist maxAge, otherwise inactive queries are
      // dropped from the cache before a restore can reuse them.
      gcTime: CACHE_MAX_AGE,
      retry: 2,
      // We drive foreground focus/online via the managers below; the defaults
      // (refetchOnReconnect / refetchOnWindowFocus) then fire on foreground and
      // reconnect, satisfying "refetch immediately on reconnect/foreground".
    },
  },
})

/** Persist the map feeds to AsyncStorage. */
export const asyncStoragePersister = createAsyncStoragePersister({
  storage: AsyncStorage,
  key: 'fogos-query-cache',
  throttleTime: 1000,
})

// The two map feeds are the ONLY queries persisted. Everything else — chiefly
// incident DETAIL (['incidents','detail',id]: history 500 + photos + hotspots)
// — stays in-memory only. Dehydrating the whole cache serializes every opened
// detail into a SINGLE AsyncStorage row, which on Android trips the ~2 MB
// SQLite CursorWindow limit and breaks the cold-start restore entirely.
const PERSISTED_FEED_KEYS: readonly (readonly [string, string])[] = [
  ['incidents', 'active'],
  ['incidents', 'recent'],
]

/**
 * Persist gate for the AsyncStorage persister: the library default (success-only)
 * AND a map-feed key match. Passed to PersistQueryClientProvider via
 * `persistOptions.dehydrateOptions.shouldDehydrateQuery`.
 */
export function shouldDehydrateQuery(query: Query): boolean {
  if (!defaultShouldDehydrateQuery(query)) return false
  const key = query.queryKey
  return PERSISTED_FEED_KEYS.some(([a, b]) => key[0] === a && key[1] === b)
}

// onlineManager ↔ NetInfo: pause polling/refetch while offline, resume on
// reconnect. Registered once at module load.
onlineManager.setEventListener((setOnline) =>
  NetInfo.addEventListener((state) => {
    setOnline(state.isConnected ?? false)
  }),
)
