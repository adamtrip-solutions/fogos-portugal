import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useIsFocused, useLocalSearchParams, useRouter } from 'expo-router'
// Vendored react-navigation (expo-router bundles it; no standalone package to
// import from). Gives the true tab-bar height incl. the home-indicator inset, so
// the map's attribution button can be lifted clear of the tab bar.
import { useBottomTabBarHeight } from 'expo-router/build/react-navigation/bottom-tabs'

import { STATUS_BUCKETS, formatRelative } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'

import { FireFilterControl } from '@/components/fire-filter-control'
import { FireMap, type FireMapRef } from '@/components/fire-map'
import { IncidentSheet, type IncidentSheetRef } from '@/components/incident-sheet'
import { MapLegend } from '@/components/map-legend'
import { Colors, Spacing } from '@/constants/theme'
import { useIncident } from '@/hooks/use-incident'
import { useIsOnline } from '@/hooks/use-is-online'
import { useMapIncidents } from '@/hooks/use-map-incidents'
import type { MapFilters } from '@/lib/fogos/map-feed'

/** The two active buckets the "Só ativos" shortcut maps to (web parity). */
const ACTIVE_BUCKETS: StatusBucket[] = ['dispatch', 'ongoing']

/** Show "Atualizado há X" once the last successful fetch is older than this. */
const STALE_AFTER_MS = 2 * 60_000

function bucketsEqual(
  a: ReadonlySet<StatusBucket>,
  b: readonly StatusBucket[],
): boolean {
  return a.size === b.length && b.every((x) => a.has(x))
}

/** Coerce the `incident` search param (string | string[] | undefined) to an id. */
function incidentParamToId(raw: string | string[] | undefined): string | null {
  const value = Array.isArray(raw) ? raw[0] : raw
  return value && value.length > 0 ? value : null
}

/**
 * Force a re-render every 30 s while offline. The `offlineStale` check reads the
 * wall clock, but nothing refetches (and so nothing re-renders) while offline —
 * without this tick the staleness pill would never appear. No timer runs once
 * back online (reconnect refetches, which re-renders on its own).
 */
function useOfflineTick(isOnline: boolean): void {
  const [, setTick] = useState(0)
  useEffect(() => {
    if (isOnline) return
    const id = setInterval(() => setTick((t) => t + 1), 30_000)
    return () => clearInterval(id)
  }, [isOnline])
}

export default function MapScreen() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const insets = useSafeAreaInsets()
  const tabBarHeight = useBottomTabBarHeight()
  const isOnline = useIsOnline()
  const router = useRouter()

  // Ephemeral filter state (in-memory, not persisted) — mirrors the web map.
  const [buckets, setBuckets] = useState<Set<StatusBucket>>(
    () => new Set(STATUS_BUCKETS),
  )
  const [maxAgeHours, setMaxAgeHours] = useState<number | null>(null)

  // "Só ativos" is a shortcut over the same bucket state: on exactly when the
  // selection is {dispatch, ongoing}. Toggling on narrows to those two; off
  // restores all five.
  const activeOnly = bucketsEqual(buckets, ACTIVE_BUCKETS)
  const setActiveOnly = useCallback((next: boolean) => {
    setBuckets(new Set(next ? ACTIVE_BUCKETS : STATUS_BUCKETS))
  }, [])

  const filters = useMemo<MapFilters>(
    () => ({ buckets, maxAgeHours }),
    [buckets, maxAgeHours],
  )
  const { incidents, loading, isError, dataUpdatedAt } = useMapIncidents(filters)

  // Selection is URL-param driven (plan 1.4): one mechanism serves map taps,
  // the `fogosportugal://incident/{id}` scheme (redirected to `/?incident=…`),
  // and the `fogosportugal.pt/?incident=…` universal link — all land as this
  // search param, so cold-start deep links select the fire declaratively.
  const params = useLocalSearchParams<{ incident?: string }>()
  const selectedId = incidentParamToId(params.incident)

  // The map tab's focus state. The incident sheet is a BottomSheetModal portaled
  // to the root provider, so it would otherwise float over the other tabs once
  // open — gate its presentation on focus (present/dismiss below).
  const isFocused = useIsFocused()

  const sheetRef = useRef<IncidentSheetRef>(null)
  const mapRef = useRef<FireMapRef>(null)
  // Set while dismissing the sheet purely because the tab lost focus, so the
  // dismiss doesn't clear the selection param (returning re-presents the fire).
  const dismissOnBlur = useRef(false)
  // The id last chosen by a direct map tap — that dot is already framed, so we
  // suppress the deep-link camera fly for it.
  const tappedId = useRef<string | null>(null)
  // The id we have already flown the camera to (fly once per deep-link select).
  const flownId = useRef<string | null>(null)

  const selected = useMemo(
    () => incidents.find((i) => i.id === selectedId) ?? null,
    [incidents, selectedId],
  )

  // Full detail for the open sheet — polled 60 s foreground-only, disabled when
  // nothing is selected. When the fire is in the feed the sheet renders instantly
  // from `selected`; for a deep-linked fire outside the loaded window `selected`
  // is null and the sheet renders purely from this detail result.
  const detailQuery = useIncident(selectedId)

  const handleSelect = useCallback(
    (id: string) => {
      tappedId.current = id
      router.setParams({ incident: id })
    },
    [router],
  )
  const handleClose = useCallback(() => {
    // A dismiss triggered by leaving the tab must keep the selection intact.
    if (dismissOnBlur.current) {
      dismissOnBlur.current = false
      return
    }
    // Clear the param so re-presenting the same fire works and the URL matches.
    router.setParams({ incident: undefined })
  }, [router])

  // Drive the modal from the selection param AND the tab focus: covers cold-start
  // deep links (param present on first mount) and taps, and hides the portaled
  // sheet when the user switches tabs — re-presenting it on return since the
  // selection param is preserved across the blur.
  useEffect(() => {
    if (!isFocused) {
      if (selectedId != null) {
        dismissOnBlur.current = true
        sheetRef.current?.dismiss()
      }
      return
    }
    if (selectedId != null) {
      // Clear the blur flag at every (re)presentation so it can only ever be
      // consumed by the dismissal it was set for. Otherwise a fast tab round-trip
      // that re-presents before the blur dismiss completes leaves the flag stale,
      // making the NEXT real swipe-close skip clearing the incident param.
      dismissOnBlur.current = false
      sheetRef.current?.present()
    } else sheetRef.current?.dismiss()
  }, [isFocused, selectedId])

  // Fly the camera to a deep-linked fire once its detail coordinates resolve —
  // needed when the fire is outside the loaded feed (older fire) and so has no
  // dot on screen yet. Taps are skipped (their dot is already framed).
  const detailCoords = detailQuery.data?.coordinates
  useEffect(() => {
    if (selectedId == null) {
      flownId.current = null
      tappedId.current = null
      return
    }
    if (tappedId.current === selectedId) return
    if (flownId.current === selectedId) return
    if (detailCoords == null) return
    mapRef.current?.focus(detailCoords)
    flownId.current = selectedId
  }, [selectedId, detailCoords])

  // Re-render on a 30 s tick while offline so the wall-clock staleness check
  // below actually flips (nothing else re-renders once polling is paused).
  useOfflineTick(isOnline)

  // Offline staleness (plan F1): while serving cached data offline and the last
  // successful update is stale, relabel the status pill instead of a new banner.
  const offlineStale =
    !isOnline &&
    dataUpdatedAt > 0 &&
    // eslint-disable-next-line react-hooks/purity -- staleness needs the wall clock; re-evaluated on every poll/state change
    Date.now() - dataUpdatedAt > STALE_AFTER_MS

  const pillText = loading
    ? 'A carregar…'
    : isError
      ? 'Sem ligação à API'
      : offlineStale
        ? `Atualizado ${formatRelative(new Date(dataUpdatedAt).toISOString())}`
        : null

  return (
    <View style={styles.container}>
      <FireMap
        ref={mapRef}
        incidents={incidents}
        isDark={scheme === 'dark'}
        onSelect={handleSelect}
        // Lift the (legally required) attribution button above the tab bar.
        bottomInset={tabBarHeight}
      />

      {pillText != null && (
        <View
          style={[
            styles.statusPill,
            { top: insets.top + Spacing.two, backgroundColor: c.backgroundElement },
          ]}
        >
          {loading && <ActivityIndicator size="small" color={c.text} />}
          <Text style={[styles.statusText, { color: c.text }]}>{pillText}</Text>
        </View>
      )}

      <View
        style={[styles.controls, { top: insets.top + Spacing.two }]}
        pointerEvents="box-none"
      >
        <FireFilterControl
          buckets={buckets}
          onBucketsChange={setBuckets}
          maxAgeHours={maxAgeHours}
          onMaxAgeChange={setMaxAgeHours}
          activeOnly={activeOnly}
          onActiveOnlyChange={setActiveOnly}
        />
        <MapLegend />
      </View>

      <IncidentSheet
        ref={sheetRef}
        incident={selected}
        detail={detailQuery.data ?? null}
        detailLoading={detailQuery.isLoading}
        detailError={detailQuery.isError}
        // Deep link to a purged/bogus id: the query succeeds with a null incident
        // and there is no list item to fall back on → the sheet shows a quiet
        // "not found" line instead of a blank sheet.
        detailNotFound={detailQuery.isSuccess && detailQuery.data == null}
        onClose={handleClose}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  statusPill: {
    position: 'absolute',
    alignSelf: 'center',
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    borderRadius: 999,
  },
  statusText: {
    fontSize: 14,
    fontWeight: '600',
  },
  controls: {
    position: 'absolute',
    right: Spacing.three,
    gap: Spacing.two,
    alignItems: 'flex-end',
  },
})
