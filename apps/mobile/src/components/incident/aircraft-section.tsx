import { StyleSheet, Text, View } from 'react-native'

import { formatRelative } from '@fogos/ui-tokens'
import type { IncidentAircraft } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

/**
 * Currently-associated aircraft (web's `AircraftSection`, "Meios aéreos
 * associados") — only the ones flagged active, each with its name/registration,
 * kind, and last-seen relative time. Renders nothing when none are active.
 */
export function AircraftSection({
  aircraft,
  c,
}: {
  aircraft: IncidentAircraft[]
  c: ThemeColors
}) {
  const active = aircraft.filter((a) => a.active)
  if (active.length === 0) return null

  return (
    <Section title="Meios aéreos associados" c={c}>
      <View style={styles.list}>
        {active.map((a) => {
          const title =
            a.name?.trim() || a.registration?.trim() || a.icao.toUpperCase()
          const subtitle =
            a.registration && a.registration !== title ? a.registration : a.kind
          return (
            <View key={a.icao} style={styles.row}>
              <View style={styles.text}>
                <Text style={[styles.title, { color: c.text }]} numberOfLines={1}>
                  {title}
                </Text>
                {subtitle && (
                  <Text
                    style={[styles.subtitle, { color: c.textSecondary }]}
                    numberOfLines={1}
                  >
                    {subtitle}
                  </Text>
                )}
              </View>
              <Text style={[styles.seen, { color: c.textSecondary }]}>
                {formatRelative(a.lastSeenAt)}
              </Text>
            </View>
          )
        })}
      </View>
    </Section>
  )
}

const styles = StyleSheet.create({
  list: {
    gap: Spacing.two,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
  },
  text: {
    flex: 1,
    gap: 1,
  },
  title: {
    fontSize: 14,
    fontWeight: '600',
  },
  subtitle: {
    fontSize: 12,
  },
  seen: {
    fontSize: 12,
  },
})
