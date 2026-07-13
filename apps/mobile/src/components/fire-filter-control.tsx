import { useCallback, useRef } from 'react'
import {
  Pressable,
  StyleSheet,
  Switch,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { BottomSheetModal, BottomSheetView } from '@gorhom/bottom-sheet'
import type { ComponentRef } from 'react'

import { STATUS_BUCKETS } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'

import { Colors, Spacing } from '@/constants/theme'
import { MapPillButton } from '@/components/map-pill-button'
import { StatusBucketChips } from '@/components/status-bucket-chips'

const ACCENT = '#FF6E02'

// Single-select "updated within" pills (hours; null = no age limit). Labels
// verbatim from web's fire-filter-control.tsx AGE_OPTIONS.
const AGE_OPTIONS: { label: string; value: number | null }[] = [
  { label: 'Tudo', value: null },
  { label: '1h', value: 1 },
  { label: '3h', value: 3 },
  { label: '6h', value: 6 },
  { label: '12h', value: 12 },
]

interface FireFilterControlProps {
  buckets: ReadonlySet<StatusBucket>
  onBucketsChange: (next: Set<StatusBucket>) => void
  maxAgeHours: number | null
  onMaxAgeChange: (h: number | null) => void
  /** "Só ativos" shortcut = buckets {dispatch, ongoing}. */
  activeOnly: boolean
  onActiveOnlyChange: (next: boolean) => void
}

/** Count of non-default filter facets, for the pill badge. */
function nonDefaultCount(
  buckets: ReadonlySet<StatusBucket>,
  maxAgeHours: number | null,
): number {
  let n = 0
  if (buckets.size !== STATUS_BUCKETS.length) n += 1
  if (maxAgeHours != null) n += 1
  return n
}

/**
 * Floating filter pill (top-right) that opens a bottom sheet with the "Só
 * ativos" shortcut, status-bucket multi-select, and the updated-within age pills
 * — the native port of web's FireFilterControl. Fully controlled; state lives in
 * the map screen (ephemeral, not persisted).
 */
export function FireFilterControl({
  buckets,
  onBucketsChange,
  maxAgeHours,
  onMaxAgeChange,
  activeOnly,
  onActiveOnlyChange,
}: FireFilterControlProps) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const sheetRef = useRef<ComponentRef<typeof BottomSheetModal>>(null)

  const badge = nonDefaultCount(buckets, maxAgeHours)

  const open = useCallback(() => sheetRef.current?.present(), [])

  const toggleBucket = useCallback(
    (bucket: StatusBucket) => {
      const next = new Set(buckets)
      if (next.has(bucket)) next.delete(bucket)
      else next.add(bucket)
      onBucketsChange(next)
    },
    [buckets, onBucketsChange],
  )

  const reset = useCallback(() => {
    onBucketsChange(new Set(STATUS_BUCKETS))
    onMaxAgeChange(null)
  }, [onBucketsChange, onMaxAgeChange])

  return (
    <>
      <Pressable onPress={open} hitSlop={8}>
        <MapPillButton
          symbol="line.3.horizontal.decrease.circle"
          fallbackGlyph="≡"
          badge={badge}
          accessibilityLabel="Filtros"
        />
      </Pressable>

      <BottomSheetModal
        ref={sheetRef}
        enableDynamicSizing
        backgroundStyle={{ backgroundColor: c.background }}
        handleIndicatorStyle={{ backgroundColor: c.textSecondary }}
      >
        <BottomSheetView style={styles.content}>
          <Text style={[styles.title, { color: c.text }]}>Filtros</Text>

          {/* Só ativos — shortcut over the bucket state */}
          <View style={[styles.switchRow, { backgroundColor: c.backgroundElement }]}>
            <Text style={[styles.switchLabel, { color: c.text }]}>Só ativos</Text>
            <Switch
              value={activeOnly}
              onValueChange={onActiveOnlyChange}
              trackColor={{ true: ACCENT }}
            />
          </View>

          {/* Estado — multi-select status buckets */}
          <Text style={[styles.sectionLabel, { color: c.textSecondary }]}>
            Estado
          </Text>
          <StatusBucketChips buckets={buckets} onToggle={toggleBucket} c={c} />

          {/* Atividade — single-select "updated within" */}
          <Text style={[styles.sectionLabel, { color: c.textSecondary }]}>
            Atividade
          </Text>
          <Text style={[styles.sectionHint, { color: c.textSecondary }]}>
            Atualizadas há menos de
          </Text>
          <View style={styles.ages}>
            {AGE_OPTIONS.map((opt) => {
              const selected = maxAgeHours === opt.value
              return (
                <Pressable
                  key={opt.label}
                  onPress={() => onMaxAgeChange(opt.value)}
                  accessibilityRole="button"
                  accessibilityState={{ selected }}
                  style={[
                    styles.agePill,
                    {
                      backgroundColor: selected ? ACCENT : c.backgroundElement,
                    },
                  ]}
                >
                  <Text
                    style={[
                      styles.agePillLabel,
                      { color: selected ? '#ffffff' : c.textSecondary },
                    ]}
                  >
                    {opt.label}
                  </Text>
                </Pressable>
              )
            })}
          </View>

          {/* Repor */}
          {badge > 0 && (
            <Pressable onPress={reset} style={styles.reset} hitSlop={8}>
              <Text style={[styles.resetLabel, { color: ACCENT }]}>Repor</Text>
            </Pressable>
          )}
        </BottomSheetView>
      </BottomSheetModal>
    </>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingBottom: Spacing.five,
    gap: Spacing.two,
  },
  title: {
    fontSize: 20,
    fontWeight: '700',
    marginBottom: Spacing.one,
  },
  switchRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
  },
  switchLabel: {
    fontSize: 15,
    fontWeight: '600',
  },
  sectionLabel: {
    fontSize: 12,
    fontWeight: '700',
    textTransform: 'uppercase',
    letterSpacing: 0.6,
    marginTop: Spacing.three,
  },
  sectionHint: {
    fontSize: 12,
    marginTop: Spacing.half,
  },
  ages: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
    marginTop: Spacing.two,
  },
  agePill: {
    borderRadius: 999,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    minWidth: 52,
    alignItems: 'center',
  },
  agePillLabel: {
    fontSize: 14,
    fontWeight: '600',
  },
  reset: {
    alignSelf: 'flex-start',
    marginTop: Spacing.three,
    paddingVertical: Spacing.one,
  },
  resetLabel: {
    fontSize: 14,
    fontWeight: '700',
  },
})
