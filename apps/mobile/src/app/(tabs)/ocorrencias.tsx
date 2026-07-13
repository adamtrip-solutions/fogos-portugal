import { useCallback, useMemo, useRef, useState } from 'react'
import {
  ActivityIndicator,
  Pressable,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useRouter } from 'expo-router'
import { SymbolView } from 'expo-symbols'
import { FlashList } from '@shopify/flash-list'
import { useInfiniteQuery } from '@tanstack/react-query'

import { buildIncidentsFilter, lisbonDateDaysAgo } from '@fogos/api-client'
import type { IncidentsWindow } from '@fogos/api-client'
import { STATUS_BUCKETS } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'

import { DistrictPickerSheet } from '@/components/district-picker-sheet'
import type { DistrictPickerSheetRef } from '@/components/district-picker-sheet'
import { IncidentCard } from '@/components/incident-card'
import { StatusBucketChips } from '@/components/status-bucket-chips'
import { Colors, Spacing } from '@/constants/theme'
import { incidentsPageQueryOptions } from '@/hooks/use-incidents-page'

const ACCENT = '#FF6E02'
const DEFAULT_WINDOW: IncidentsWindow = '3d'

// Window pills — labels copied verbatim from web's WINDOW_PILLS.
const WINDOW_PILLS: { label: string; value: IncidentsWindow }[] = [
  { label: 'Hoje', value: '1d' },
  { label: '3 dias', value: '3d' },
  { label: '7 dias', value: '7d' },
  { label: '30 dias', value: '30d' },
  { label: 'Tudo', value: 'all' },
]

export default function OcorrenciasScreen() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const insets = useSafeAreaInsets()
  const router = useRouter()

  // Filter state is local component state — it persists naturally across tab
  // switches (the screen stays mounted) and is intentionally NOT synced to a URL
  // (native has no address bar). Defaults mirror web: 3-day window, all buckets.
  const [window, setWindow] = useState<IncidentsWindow>(DEFAULT_WINDOW)
  const [buckets, setBuckets] = useState<Set<StatusBucket>>(
    () => new Set(STATUS_BUCKETS),
  )
  const [district, setDistrict] = useState<string | null>(null)

  const districtSheetRef = useRef<DistrictPickerSheetRef>(null)

  // Canonicalise buckets to STATUS_BUCKETS order so the filter (and thus the
  // query key) stays structurally stable regardless of toggle order.
  const orderedBuckets = useMemo(
    () => STATUS_BUCKETS.filter((b) => buckets.has(b)),
    [buckets],
  )
  // Recompute the Lisbon calendar day on every render so the window's `after`
  // cutoff (and thus the query key) rolls over at midnight — tab screens never
  // unmount, so a frozen value would leave "Hoje" stuck on yesterday. The Intl
  // call is cheap (web recomputes per render too).
  const lisbonToday = lisbonDateDaysAgo(0)
  const filter = useMemo(
    () =>
      buildIncidentsFilter({
        window,
        buckets: orderedBuckets,
        district: district ?? undefined,
      }),
    // `lisbonToday` is a rollover sentinel: buildIncidentsFilter reads the Lisbon
    // day internally, so a new day must invalidate the memo (and the query key).
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional midnight-rollover sentinel; not read in the body
    [window, orderedBuckets, district, lisbonToday],
  )

  const query = useInfiniteQuery(incidentsPageQueryOptions(filter))
  const incidents = useMemo(
    () => query.data?.pages.flatMap((p) => p.nodes) ?? [],
    [query.data],
  )
  const totalCount = query.data?.pages[0]?.totalCount

  const nonDefault =
    window !== DEFAULT_WINDOW ||
    buckets.size !== STATUS_BUCKETS.length ||
    district != null

  const toggleBucket = useCallback((bucket: StatusBucket) => {
    setBuckets((prev) => {
      const next = new Set(prev)
      if (next.has(bucket)) next.delete(bucket)
      else next.add(bucket)
      // Emptying the selection would make buildIncidentsFilter omit statusCodes
      // (= show everything) while every chip renders unchecked — an inverted,
      // confusing result. Mirror web's effective semantics: reset to all five.
      if (next.size === 0) return new Set(STATUS_BUCKETS)
      return next
    })
  }, [])

  const clearFilters = useCallback(() => {
    setWindow(DEFAULT_WINDOW)
    setBuckets(new Set(STATUS_BUCKETS))
    setDistrict(null)
  }, [])

  // Tap → open the map tab with the fire preselected (same URL param the map
  // reads for taps, deep links, and universal links).
  const openIncident = useCallback(
    (id: string) => {
      router.navigate({ pathname: '/', params: { incident: id } })
    },
    [router],
  )

  const loadMore = useCallback(() => {
    if (query.hasNextPage && !query.isFetchingNextPage) {
      void query.fetchNextPage()
    }
  }, [query])

  return (
    <View style={[styles.container, { backgroundColor: c.background }]}>
      {/* Filter header */}
      <View style={styles.filters}>
        <View style={styles.pills}>
          {WINDOW_PILLS.map((pill) => {
            const selected = window === pill.value
            return (
              <Pressable
                key={pill.value}
                onPress={() => setWindow(pill.value)}
                accessibilityRole="button"
                accessibilityState={{ selected }}
                style={[
                  styles.pill,
                  { backgroundColor: selected ? ACCENT : c.backgroundElement },
                ]}
              >
                <Text
                  style={[
                    styles.pillLabel,
                    { color: selected ? '#ffffff' : c.textSecondary },
                  ]}
                >
                  {pill.label}
                </Text>
              </Pressable>
            )
          })}
        </View>

        <StatusBucketChips buckets={buckets} onToggle={toggleBucket} c={c} />

        <View style={styles.districtRow}>
          <Pressable
            onPress={() => districtSheetRef.current?.present()}
            accessibilityRole="button"
            accessibilityLabel="Distrito"
            style={[styles.district, { backgroundColor: c.backgroundElement }]}
          >
            <Text style={[styles.districtValue, { color: c.text }]} numberOfLines={1}>
              {district ?? 'Todos os distritos'}
            </Text>
            <SymbolView
              name="chevron.down"
              size={13}
              tintColor={c.textSecondary}
              fallback={
                <Text style={[styles.districtChevron, { color: c.textSecondary }]}>
                  ▾
                </Text>
              }
            />
          </Pressable>

          {nonDefault && (
            <Pressable onPress={clearFilters} hitSlop={8}>
              <Text style={[styles.clear, { color: ACCENT }]}>Limpar</Text>
            </Pressable>
          )}
        </View>
      </View>

      {/* List */}
      <FlashList
        data={incidents}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <IncidentCard
            incident={item}
            scheme={scheme}
            c={c}
            onPress={openIncident}
          />
        )}
        ItemSeparatorComponent={() => <View style={styles.separator} />}
        contentContainerStyle={{
          paddingHorizontal: Spacing.four,
          paddingTop: Spacing.two,
          paddingBottom: insets.bottom + Spacing.four,
        }}
        onEndReached={loadMore}
        onEndReachedThreshold={0.5}
        keyboardShouldPersistTaps="handled"
        ListEmptyComponent={
          query.isLoading ? (
            <View style={styles.state}>
              <ActivityIndicator color={c.textSecondary} />
            </View>
          ) : query.isError ? (
            <View style={styles.state}>
              <Text style={[styles.stateText, { color: c.textSecondary }]}>
                Não foi possível carregar as ocorrências.
              </Text>
              <Pressable onPress={() => query.refetch()} hitSlop={8}>
                <Text style={[styles.stateAction, { color: ACCENT }]}>
                  Tentar novamente
                </Text>
              </Pressable>
            </View>
          ) : (
            <View style={styles.state}>
              <Text style={[styles.stateText, { color: c.textSecondary }]}>
                Sem ocorrências para os filtros selecionados.
              </Text>
              {nonDefault && (
                <Pressable onPress={clearFilters} hitSlop={8}>
                  <Text style={[styles.stateAction, { color: ACCENT }]}>
                    Limpar filtros
                  </Text>
                </Pressable>
              )}
            </View>
          )
        }
        ListFooterComponent={
          incidents.length > 0 ? (
            <View style={styles.footer}>
              {query.hasNextPage && (
                <Pressable
                  onPress={loadMore}
                  disabled={query.isFetchingNextPage}
                  style={[
                    styles.loadMore,
                    { backgroundColor: c.backgroundElement },
                    query.isFetchingNextPage && styles.loadMoreBusy,
                  ]}
                >
                  {query.isFetchingNextPage && (
                    <ActivityIndicator size="small" color={c.text} />
                  )}
                  <Text style={[styles.loadMoreLabel, { color: c.text }]}>
                    Carregar mais
                  </Text>
                </Pressable>
              )}
              {totalCount != null && (
                <Text style={[styles.count, { color: c.textSecondary }]}>
                  {incidents.length} de {totalCount} ocorrências
                </Text>
              )}
            </View>
          ) : null
        }
      />

      <DistrictPickerSheet
        ref={districtSheetRef}
        selected={district}
        onSelect={setDistrict}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  filters: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
    paddingBottom: Spacing.three,
    gap: Spacing.three,
  },
  pills: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  pill: {
    borderRadius: 999,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
  },
  pillLabel: {
    fontSize: 14,
    fontWeight: '600',
  },
  districtRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
  },
  district: {
    flex: 1,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.two,
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
  },
  districtValue: {
    flex: 1,
    fontSize: 15,
    fontWeight: '500',
  },
  districtChevron: {
    fontSize: 12,
    fontWeight: '700',
  },
  clear: {
    fontSize: 14,
    fontWeight: '700',
  },
  separator: {
    height: Spacing.two,
  },
  state: {
    alignItems: 'center',
    gap: Spacing.three,
    paddingVertical: Spacing.six,
    paddingHorizontal: Spacing.four,
  },
  stateText: {
    fontSize: 14,
    textAlign: 'center',
    lineHeight: 20,
  },
  stateAction: {
    fontSize: 14,
    fontWeight: '600',
  },
  footer: {
    alignItems: 'center',
    gap: Spacing.two,
    paddingTop: Spacing.four,
  },
  loadMore: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.four,
    paddingVertical: Spacing.two,
  },
  loadMoreBusy: {
    opacity: 0.6,
  },
  loadMoreLabel: {
    fontSize: 14,
    fontWeight: '600',
  },
  count: {
    fontSize: 12,
    fontVariant: ['tabular-nums'],
  },
})
