import { useCallback, useRef } from 'react'
import { Pressable, StyleSheet, Text, View, useColorScheme } from 'react-native'
import { BottomSheetModal, BottomSheetView } from '@gorhom/bottom-sheet'
import type { ComponentRef } from 'react'

import { STATUS_BUCKET_COLOR, STATUS_BUCKET_LABEL } from '@fogos/ui-tokens'
import type { StatusBucket } from '@fogos/ui-tokens'

import { Colors, Spacing } from '@/constants/theme'
import { MapPillButton } from '@/components/map-pill-button'

// Bucket order + labels ported verbatim from web's map-legend.tsx ROWS.
const ROWS: StatusBucket[] = [
  'dispatch',
  'ongoing',
  'resolving',
  'vigilancia',
  'done',
]

/** Floating legend pill (under the filter pill) opening a small colour-key sheet. */
export function MapLegend() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const sheetRef = useRef<ComponentRef<typeof BottomSheetModal>>(null)

  const open = useCallback(() => sheetRef.current?.present(), [])

  return (
    <>
      <Pressable onPress={open} hitSlop={8}>
        <MapPillButton
          symbol="info.circle"
          fallbackGlyph="i"
          accessibilityLabel="Legenda"
        />
      </Pressable>

      <BottomSheetModal
        ref={sheetRef}
        enableDynamicSizing
        backgroundStyle={{ backgroundColor: c.background }}
        handleIndicatorStyle={{ backgroundColor: c.textSecondary }}
      >
        <BottomSheetView style={styles.content}>
          <Text style={[styles.title, { color: c.text }]}>Legenda</Text>
          {ROWS.map((bucket) => (
            <View key={bucket} style={styles.row}>
              <View
                style={[styles.dot, { backgroundColor: STATUS_BUCKET_COLOR[bucket] }]}
              />
              <Text style={[styles.label, { color: c.text }]}>
                {STATUS_BUCKET_LABEL[bucket]}
              </Text>
            </View>
          ))}
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
    marginBottom: Spacing.two,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
  },
  dot: {
    width: 14,
    height: 14,
    borderRadius: 999,
  },
  label: {
    fontSize: 15,
  },
})
