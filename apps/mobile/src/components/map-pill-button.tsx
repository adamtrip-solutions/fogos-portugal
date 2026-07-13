import { StyleSheet, Text, View, useColorScheme } from 'react-native'
import { SymbolView } from 'expo-symbols'
import type { SFSymbol } from 'expo-symbols'
import type { ComponentProps } from 'react'

import { Colors } from '@/constants/theme'

/**
 * Floating round map control — the pill styling language shared with the map's
 * status pill (solid `backgroundElement`, fully rounded, shadowed). Renders an
 * SF Symbol on iOS and a text-glyph fallback elsewhere, with an optional count
 * badge in the top-right corner.
 */
export function MapPillButton({
  symbol,
  fallbackGlyph,
  badge,
  accessibilityLabel,
}: {
  symbol: SFSymbol
  /** Text glyph shown where SF Symbols are unavailable (Android/web). */
  fallbackGlyph: string
  /** Non-zero shows a small count badge. */
  badge?: number
  accessibilityLabel: string
}) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]

  return (
    <View
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      style={[styles.pill, { backgroundColor: c.backgroundElement }]}
    >
      <SymbolView
        name={symbol}
        size={22}
        tintColor={c.text}
        fallback={
          <Text style={[styles.fallback, { color: c.text }]}>{fallbackGlyph}</Text>
        }
      />
      {badge != null && badge > 0 && (
        <View style={[styles.badge, { borderColor: c.background }]}>
          <Text style={styles.badgeText}>{badge}</Text>
        </View>
      )}
    </View>
  )
}

/** Props a `Pressable` wrapper should spread onto itself, kept in one place. */
export type MapPillButtonProps = ComponentProps<typeof MapPillButton>

const ACCENT = '#FF6E02'

const styles = StyleSheet.create({
  pill: {
    width: 44,
    height: 44,
    borderRadius: 999,
    alignItems: 'center',
    justifyContent: 'center',
    shadowColor: '#000',
    shadowOpacity: 0.18,
    shadowRadius: 8,
    shadowOffset: { width: 0, height: 2 },
    elevation: 4,
  },
  fallback: {
    fontSize: 20,
    fontWeight: '700',
  },
  badge: {
    position: 'absolute',
    top: -2,
    right: -2,
    minWidth: 18,
    height: 18,
    paddingHorizontal: 4,
    borderRadius: 999,
    borderWidth: 2,
    backgroundColor: ACCENT,
    alignItems: 'center',
    justifyContent: 'center',
  },
  badgeText: {
    color: '#ffffff',
    fontSize: 11,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
})
