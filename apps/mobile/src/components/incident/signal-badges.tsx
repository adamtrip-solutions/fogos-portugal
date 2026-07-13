import { StyleSheet, Text, View } from 'react-native'

import { criticalReasonLabel } from '@fogos/ui-tokens'

import { Spacing } from '@/constants/theme'
import type { ThemeColors } from './section'

/** Solid red for the "Condições críticas" badge — matches web's CRITICAL_RED. */
const CRITICAL_RED = '#991b1b'

// Tinted badge palettes (light/dark), mirroring the web panel's amber/red pills.
const AMBER = { light: { bg: 'rgba(245,158,11,0.15)', fg: '#b45309' }, dark: { bg: 'rgba(245,158,11,0.22)', fg: '#fcd34d' } }
const RED = { light: { bg: 'rgba(239,68,68,0.15)', fg: '#b91c1c' }, dark: { bg: 'rgba(239,68,68,0.22)', fg: '#fca5a5' } }

/**
 * The escalation / rekindle / critical-conditions badge row (web's
 * `SignalBadges`). Critical reasons render as a small muted line beneath the
 * badges (the mobile stand-in for web's tooltip). Renders nothing when no
 * signal is set.
 */
export function SignalBadges({
  escalating,
  rekindle,
  criticalConditions,
  reasons,
  scheme,
  c,
}: {
  escalating: boolean
  rekindle: boolean
  criticalConditions: boolean
  reasons: string[]
  scheme: 'light' | 'dark'
  c: ThemeColors
}) {
  if (!escalating && !rekindle && !criticalConditions) return null

  const amber = AMBER[scheme]
  const red = RED[scheme]

  return (
    <View style={styles.wrap}>
      <View style={styles.row}>
        {escalating && (
          <View style={[styles.badge, { backgroundColor: amber.bg }]}>
            <Text style={[styles.badgeText, { color: amber.fg }]}>Em escalada</Text>
          </View>
        )}
        {rekindle && (
          <View style={[styles.badge, { backgroundColor: red.bg }]}>
            <Text style={[styles.badgeText, { color: red.fg }]}>Reacendimento</Text>
          </View>
        )}
        {criticalConditions && (
          <View style={[styles.badge, { backgroundColor: CRITICAL_RED }]}>
            <Text style={[styles.badgeText, { color: '#ffffff' }]}>
              Condições críticas
            </Text>
          </View>
        )}
      </View>
      {criticalConditions && reasons.length > 0 && (
        <Text style={[styles.reasons, { color: c.textSecondary }]}>
          {reasons.map((k) => criticalReasonLabel(k)).join(' · ')}
        </Text>
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: {
    gap: Spacing.one,
  },
  row: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'center',
    gap: Spacing.one + 2,
  },
  badge: {
    borderRadius: 999,
    paddingHorizontal: Spacing.two,
    paddingVertical: Spacing.half,
  },
  badgeText: {
    fontSize: 12,
    fontWeight: '700',
  },
  reasons: {
    fontSize: 12,
  },
})
