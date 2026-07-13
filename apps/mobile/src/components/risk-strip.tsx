import { StyleSheet, Text, View } from 'react-native'

import { riskStyle } from '@fogos/ui-tokens'
import type { ConcelhoRiskDay } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import type { ThemeColors } from '@/components/incident/section'

// Weekday + day-of-month label, e.g. "seg, 7" — mirrors web's RiskStrip dayFmt.
const dayFmt = new Intl.DateTimeFormat('pt-PT', {
  weekday: 'short',
  day: 'numeric',
})

/**
 * The 5-day per-concelho risk strip — the mobile port of web's `RiskStrip`: one
 * cell per horizon with its weekday label, a colour-coded level bubble, and the
 * pt-PT level label. Shared by the concelho profile screen.
 */
export function RiskStrip({
  risk,
  c,
}: {
  risk: ConcelhoRiskDay[]
  c: ThemeColors
}) {
  return (
    <View style={styles.row}>
      {risk.slice(0, 5).map((r) => {
        const style = riskStyle(r.level)
        return (
          <View
            key={r.date}
            style={[styles.cell, { backgroundColor: c.backgroundElement }]}
          >
            <Text style={[styles.day, { color: c.textSecondary }]} numberOfLines={1}>
              {dayFmt.format(new Date(`${r.date}T00:00:00`))}
            </Text>
            <View style={[styles.bubble, { backgroundColor: style.bg }]}>
              <Text style={styles.bubbleText}>{r.level}</Text>
            </View>
            <Text style={[styles.label, { color: c.textSecondary }]} numberOfLines={2}>
              {r.label || style.label}
            </Text>
          </View>
        )
      })}
    </View>
  )
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: Spacing.two,
  },
  cell: {
    flex: 1,
    alignItems: 'center',
    gap: Spacing.one + 2,
    borderRadius: Spacing.three,
    paddingVertical: Spacing.two,
    paddingHorizontal: Spacing.half,
  },
  day: {
    fontSize: 11,
    fontWeight: '600',
  },
  bubble: {
    width: 34,
    height: 34,
    borderRadius: 999,
    alignItems: 'center',
    justifyContent: 'center',
  },
  bubbleText: {
    color: '#ffffff',
    fontSize: 15,
    fontWeight: '700',
  },
  label: {
    fontSize: 10,
    lineHeight: 13,
    textAlign: 'center',
  },
})
