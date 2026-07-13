import { Pressable, StyleSheet, Text, View } from 'react-native'
import { SymbolView } from 'expo-symbols'
import type { SFSymbol } from 'expo-symbols'

import {
  formatRelative,
  incidentTitle,
  locationParts,
  statusColorForCode,
} from '@fogos/ui-tokens'
import type { IncidentListItem } from '@fogos/api-client'

import { Colors, Spacing } from '@/constants/theme'

/** Widened theme-color bag (either scheme fits). */
type ThemeColors = { [K in keyof typeof Colors.light]: string }

// Resource glyphs mirror web's ResourceCell (Users / Truck / Plane). Counts show
// the number, or "—" when 0/undisclosed — same as web.
const RESOURCE_ICONS: {
  key: 'man' | 'terrain' | 'aerial'
  symbol: SFSymbol
  glyph: string
}[] = [
  { key: 'man', symbol: 'person.2.fill', glyph: 'Op' },
  { key: 'terrain', symbol: 'truck.box.fill', glyph: 'Ter' },
  { key: 'aerial', symbol: 'airplane', glyph: 'Aé' },
]

// Signal/flag pills — palette + verbatim labels mirror web's IncidentFlags and
// the sheet's SignalBadges. Amber = warning, red = severe.
const AMBER = {
  light: { bg: 'rgba(245,158,11,0.15)', fg: '#b45309' },
  dark: { bg: 'rgba(245,158,11,0.22)', fg: '#fcd34d' },
}
const RED = {
  light: { bg: 'rgba(239,68,68,0.15)', fg: '#b91c1c' },
  dark: { bg: 'rgba(239,68,68,0.22)', fg: '#fca5a5' },
}

/**
 * Ocorrências list card — the mobile port of web's incident-row.tsx mobile-card
 * variant, enriched with the resource counts and flag badges web's incidents
 * table shows (ResourceCell + IncidentFlags). Tapping opens the map tab with the
 * fire preselected. pt-PT throughout.
 */
export function IncidentCard({
  incident,
  scheme,
  c,
  onPress,
}: {
  incident: IncidentListItem
  scheme: 'light' | 'dark'
  c: ThemeColors
  onPress: (id: string) => void
}) {
  const place =
    locationParts(incident.freguesia, incident.concelho, incident.district) ||
    incident.location

  return (
    <Pressable
      onPress={() => onPress(incident.id)}
      accessibilityRole="button"
      accessibilityLabel={incidentTitle(incident)}
      style={[styles.card, { backgroundColor: c.backgroundElement }]}
    >
      <View
        style={[
          styles.dot,
          { backgroundColor: statusColorForCode(incident.status.code) },
        ]}
      />
      <View style={styles.body}>
        <Text numberOfLines={1} style={[styles.title, { color: c.text }]}>
          {incidentTitle(incident)}
        </Text>
        {place.length > 0 && (
          <Text numberOfLines={1} style={[styles.place, { color: c.textSecondary }]}>
            {place}
          </Text>
        )}
        <Text style={[styles.meta, { color: c.textSecondary }]}>
          {incident.status.label} · {formatRelative(incident.occurredAt)}
        </Text>

        <View style={styles.resources}>
          {RESOURCE_ICONS.map(({ key, symbol, glyph }) => {
            const value = incident.resources[key]
            return (
              <View key={key} style={styles.resource}>
                <SymbolView
                  name={symbol}
                  size={12}
                  tintColor={c.textSecondary}
                  fallback={
                    <Text style={[styles.resourceGlyph, { color: c.textSecondary }]}>
                      {glyph}
                    </Text>
                  }
                />
                <Text style={[styles.resourceValue, { color: c.textSecondary }]}>
                  {value > 0 ? value : '—'}
                </Text>
              </View>
            )
          })}
        </View>

        <Flags incident={incident} scheme={scheme} />
      </View>

      <SymbolView
        name="chevron.right"
        size={13}
        tintColor={c.textSecondary}
        fallback={<Text style={[styles.chevron, { color: c.textSecondary }]}>›</Text>}
      />
    </Pressable>
  )
}

/** Flag pill row — renders nothing when no flag is set (web parity). */
function Flags({
  incident,
  scheme,
}: {
  incident: IncidentListItem
  scheme: 'light' | 'dark'
}) {
  const amber = AMBER[scheme]
  const red = RED[scheme]
  const pills: { label: string; bg: string; fg: string }[] = []
  if (incident.important) {
    pills.push({ label: 'Importante', bg: amber.bg, fg: amber.fg })
  }
  if (incident.signals.escalating) {
    pills.push({ label: 'Em escalada', bg: amber.bg, fg: amber.fg })
  }
  if (incident.signals.rekindle) {
    pills.push({ label: 'Reacendimento', bg: red.bg, fg: red.fg })
  }
  if (incident.signals.criticalConditions) {
    pills.push({ label: 'Condições críticas', bg: red.bg, fg: red.fg })
  }
  if (pills.length === 0) return null

  return (
    <View style={styles.flags}>
      {pills.map((p) => (
        <View key={p.label} style={[styles.flag, { backgroundColor: p.bg }]}>
          <Text style={[styles.flagText, { color: p.fg }]}>{p.label}</Text>
        </View>
      ))}
    </View>
  )
}

const styles = StyleSheet.create({
  card: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: Spacing.three,
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
  },
  dot: {
    width: 10,
    height: 10,
    borderRadius: 999,
    marginTop: 5,
  },
  body: {
    flex: 1,
    gap: Spacing.half,
  },
  title: {
    fontSize: 15,
    fontWeight: '600',
  },
  place: {
    fontSize: 13,
  },
  meta: {
    fontSize: 13,
  },
  resources: {
    flexDirection: 'row',
    gap: Spacing.three,
    marginTop: Spacing.half,
  },
  resource: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.one,
  },
  resourceGlyph: {
    fontSize: 11,
    fontWeight: '600',
  },
  resourceValue: {
    fontSize: 12,
    fontVariant: ['tabular-nums'],
  },
  chevron: {
    fontSize: 18,
    fontWeight: '600',
  },
  flags: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.one + 2,
    marginTop: Spacing.half,
  },
  flag: {
    borderRadius: 999,
    paddingHorizontal: Spacing.two,
    paddingVertical: Spacing.half,
  },
  flagText: {
    fontSize: 11,
    fontWeight: '700',
  },
})
