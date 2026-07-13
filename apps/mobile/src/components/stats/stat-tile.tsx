import { StyleSheet, Text, View } from 'react-native'
import { SymbolView } from 'expo-symbols'
import type { SFSymbol } from 'expo-symbols'

import { Spacing } from '@/constants/theme'
import type { ThemeColors } from '@/components/incident/section'

/** Signed-change chip tone (good = green, bad = red, neutral = muted). */
export type DeltaTone = 'good' | 'bad' | 'neutral'

const DELTA_BG: Record<DeltaTone, string> = {
  good: 'rgba(16,185,129,0.15)',
  bad: 'rgba(239,68,68,0.15)',
  neutral: 'rgba(128,128,128,0.15)',
}
const DELTA_FG: Record<DeltaTone, string> = {
  good: '#10b981',
  bad: '#ef4444',
  neutral: '#8a8f98',
}

/**
 * Mobile port of web's glass `StatTile`: a big tabular value over a label, with
 * an optional SF-symbol glyph, delta chip and hint. Shared by the dashboard
 * header grid and the response-time medians. `flexBasis` lets callers lay tiles
 * out 2-per-row or full-width in a wrapping row.
 */
export function StatTile({
  label,
  value,
  hint,
  symbol,
  glyph,
  delta,
  full,
  c,
}: {
  label: string
  value: string
  hint?: string
  symbol?: SFSymbol
  glyph?: string
  delta?: { text: string; tone: DeltaTone }
  /** Span the full row instead of sharing it two-up. */
  full?: boolean
  c: ThemeColors
}) {
  return (
    <View
      style={[
        styles.tile,
        { backgroundColor: c.backgroundElement },
        full ? styles.full : styles.half,
      ]}
    >
      <View style={styles.top}>
        {symbol ? (
          <SymbolView
            name={symbol}
            size={15}
            tintColor={c.textSecondary}
            fallback={
              glyph ? (
                <Text style={[styles.glyph, { color: c.textSecondary }]}>{glyph}</Text>
              ) : undefined
            }
          />
        ) : (
          <View />
        )}
        {delta && (
          <View style={[styles.delta, { backgroundColor: DELTA_BG[delta.tone] }]}>
            <Text style={[styles.deltaText, { color: DELTA_FG[delta.tone] }]}>
              {delta.text}
            </Text>
          </View>
        )}
      </View>
      <Text style={[styles.value, { color: c.text }]} numberOfLines={1}>
        {value}
      </Text>
      <Text style={[styles.label, { color: c.textSecondary }]}>{label}</Text>
      {hint && <Text style={[styles.hint, { color: c.textSecondary }]}>{hint}</Text>}
    </View>
  )
}

const styles = StyleSheet.create({
  tile: {
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.half,
  },
  half: {
    flexGrow: 1,
    flexBasis: '47%',
  },
  full: {
    flexGrow: 1,
    flexBasis: '100%',
  },
  top: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    minHeight: 16,
  },
  glyph: {
    fontSize: 13,
    fontWeight: '700',
  },
  delta: {
    borderRadius: 999,
    paddingHorizontal: Spacing.one + 2,
    paddingVertical: 1,
  },
  deltaText: {
    fontSize: 11,
    fontWeight: '700',
  },
  value: {
    marginTop: Spacing.half,
    fontSize: 24,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
  label: {
    fontSize: 12,
  },
  hint: {
    fontSize: 11,
  },
})
