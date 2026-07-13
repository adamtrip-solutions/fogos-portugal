import { StyleSheet, Text, View } from 'react-native'

import { formatWarningValidity, warningLevelColor } from '@fogos/ui-tokens'
import type { WeatherWarning } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import type { ThemeColors } from '@/components/incident/section'

/**
 * One IPMA awareness-warning row — a level-coloured left border, a level badge,
 * the awareness type, a validity stamp, and the optional narrative. Shared by the
 * Avisos screen (grouped by district) and the concelho profile's warnings section.
 */
export function WarningRow({
  warning,
  c,
}: {
  warning: WeatherWarning
  c: ThemeColors
}) {
  const color = warningLevelColor(warning.level)
  return (
    <View style={[styles.row, { borderLeftColor: color }]}>
      <View style={styles.rowHead}>
        <View style={[styles.badge, { backgroundColor: `${color}22` }]}>
          <Text style={[styles.badgeText, { color }]}>{warning.levelPt}</Text>
        </View>
        <Text style={[styles.type, { color: c.text }]}>{warning.awarenessType}</Text>
        <Text style={[styles.validity, { color: c.textSecondary }]}>
          {formatWarningValidity(warning.endsAt)}
        </Text>
      </View>
      {warning.text ? (
        <Text style={[styles.text, { color: c.textSecondary }]}>{warning.text}</Text>
      ) : null}
    </View>
  )
}

const styles = StyleSheet.create({
  row: {
    borderLeftWidth: 3,
    paddingLeft: Spacing.three,
    gap: Spacing.one,
  },
  rowHead: {
    flexDirection: 'row',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  badge: {
    borderRadius: 999,
    paddingHorizontal: Spacing.two,
    paddingVertical: 1,
  },
  badgeText: {
    fontSize: 11,
    fontWeight: '700',
  },
  type: {
    fontSize: 15,
    fontWeight: '500',
    flexShrink: 1,
  },
  validity: {
    marginLeft: 'auto',
    fontSize: 12,
    fontVariant: ['tabular-nums'],
  },
  text: {
    fontSize: 13,
    lineHeight: 19,
  },
})
