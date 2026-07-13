import { Pressable, StyleSheet, Text, View } from 'react-native'
import { SymbolView } from 'expo-symbols'

import {
  STATUS_BUCKETS,
  STATUS_BUCKET_COLOR,
  STATUS_BUCKET_LABEL,
} from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'

import { Colors, Spacing } from '@/constants/theme'

const ACCENT = '#FF6E02'

/** Widened theme-color bag (either scheme fits). */
type ThemeColors = { [K in keyof typeof Colors.light]: string }

/**
 * Multi-select status-bucket chip row (colored dot + pt-PT label + checkmark),
 * shared by the map's FireFilterControl and the Ocorrências filter header so
 * both stay in visual lockstep. Fully controlled; the parent owns the selection.
 */
export function StatusBucketChips({
  buckets,
  onToggle,
  c,
}: {
  buckets: ReadonlySet<StatusBucket>
  onToggle: (bucket: StatusBucket) => void
  c: ThemeColors
}) {
  return (
    <View style={styles.chips}>
      {STATUS_BUCKETS.map((bucket) => {
        const checked = buckets.has(bucket)
        return (
          <Pressable
            key={bucket}
            onPress={() => onToggle(bucket)}
            accessibilityRole="checkbox"
            accessibilityState={{ checked }}
            style={[
              styles.chip,
              {
                backgroundColor: checked
                  ? c.backgroundSelected
                  : c.backgroundElement,
                borderColor: checked ? ACCENT : 'transparent',
                opacity: checked ? 1 : 0.55,
              },
            ]}
          >
            <View
              style={[styles.dot, { backgroundColor: STATUS_BUCKET_COLOR[bucket] }]}
            />
            <Text style={[styles.chipLabel, { color: c.text }]}>
              {STATUS_BUCKET_LABEL[bucket]}
            </Text>
            {checked && (
              <SymbolView
                name="checkmark"
                size={13}
                tintColor={ACCENT}
                fallback={<Text style={styles.check}>✓</Text>}
              />
            )}
          </Pressable>
        )
      })}
    </View>
  )
}

const styles = StyleSheet.create({
  chips: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    borderRadius: 999,
    borderWidth: 1.5,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
  },
  dot: {
    width: 10,
    height: 10,
    borderRadius: 999,
  },
  chipLabel: {
    fontSize: 14,
    fontWeight: '600',
  },
  check: {
    fontSize: 12,
    fontWeight: '700',
    color: ACCENT,
  },
})
