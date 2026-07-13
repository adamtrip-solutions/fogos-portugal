import { useSyncExternalStore } from 'react'
import { onlineManager } from '@tanstack/react-query'

/**
 * Subscribes to TanStack's `onlineManager` (wired to NetInfo in lib/query.ts) so
 * a component re-renders when connectivity flips. Used to surface offline
 * staleness on the map without a second NetInfo listener.
 */
export function useIsOnline(): boolean {
  return useSyncExternalStore(
    (onChange) => onlineManager.subscribe(onChange),
    () => onlineManager.isOnline(),
    () => true,
  )
}
