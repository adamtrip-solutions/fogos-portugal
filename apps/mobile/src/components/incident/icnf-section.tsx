import { StyleSheet, Text, View } from 'react-native'

import { formatHectares } from '@fogos/ui-tokens'
import type { IncidentIcnf } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

/**
 * ICNF cause / cause-type + burn area (web's `IcnfSection`, "Causa e área
 * ardida"). Renders nothing when none of the three fields is present.
 */
export function IcnfSection({
  icnf,
  c,
}: {
  icnf: IncidentIcnf
  c: ThemeColors
}) {
  const rows: { label: string; value: string }[] = []
  if (icnf.causeType) rows.push({ label: 'Tipo de causa', value: icnf.causeType })
  if (icnf.cause) rows.push({ label: 'Causa', value: icnf.cause })
  if (icnf.burnArea?.total != null) {
    rows.push({ label: 'Área ardida', value: formatHectares(icnf.burnArea.total) })
  }

  if (rows.length === 0) return null

  return (
    <Section title="Causa e área ardida" c={c}>
      <View style={styles.list}>
        {rows.map((r) => (
          <View key={r.label} style={styles.row}>
            <Text style={[styles.label, { color: c.textSecondary }]}>
              {r.label}
            </Text>
            <Text style={[styles.value, { color: c.text }]}>{r.value}</Text>
          </View>
        ))}
      </View>
    </Section>
  )
}

const styles = StyleSheet.create({
  list: {
    gap: Spacing.one,
  },
  row: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: Spacing.three,
  },
  label: {
    fontSize: 14,
  },
  value: {
    flexShrink: 1,
    textAlign: 'right',
    fontSize: 14,
    fontWeight: '600',
  },
})
