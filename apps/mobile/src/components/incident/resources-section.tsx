import { StyleSheet, Text, View } from 'react-native'

import { hasResource } from '@fogos/ui-tokens'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

/** The three always-visible tiles (0 is real data, not absence). Web parity. */
const TILES = [
  { key: 'man', label: 'Operacionais' },
  { key: 'terrain', label: 'Meios terrestres' },
  { key: 'aerial', label: 'Meios aéreos' },
] as const

/**
 * Aerial breakdown + aquatic lines shown under the tiles when present. Web
 * fetches these fields but never renders them; the pt-PT labels here are
 * authored for the mobile panel (there is no verbatim web string to copy).
 */
const EXTRA_LINES = [
  { key: 'aquatic', label: 'Meios aquáticos' },
  { key: 'heliFight', label: 'Helicópteros de combate' },
  { key: 'heliCoord', label: 'Helicópteros de coordenação' },
  { key: 'planeFight', label: 'Aviões de combate' },
] as const

export interface SheetResources {
  man: number
  terrain: number
  aerial: number
  aquatic: number
  /** Detail-only fields; absent while only the list item is loaded. */
  heliFight?: number
  heliCoord?: number
  planeFight?: number
}

/**
 * "Meios no terreno" — the operacionais/terrestres/aéreos tiles, plus the
 * aquatic + heli/coord/plane breakdown lines when disclosed. Renders the
 * ANEPC-undisclosed note when all three tiles read `-1` (web parity).
 */
export function ResourcesSection({
  resources,
  c,
}: {
  resources: SheetResources
  c: ThemeColors
}) {
  const allUnknown = TILES.every(({ key }) => resources[key] < 0)
  const extras = EXTRA_LINES.filter((e) => hasResource(resources[e.key] ?? 0))

  return (
    <Section title="Meios no terreno" c={c}>
      <View style={styles.tiles}>
        {TILES.map(({ key, label }) => {
          const value = resources[key]
          return (
            <View
              key={key}
              style={[styles.tile, { backgroundColor: c.backgroundElement }]}
            >
              <Text style={[styles.tileValue, { color: c.text }]}>
                {value < 0 ? '—' : value}
              </Text>
              <Text style={[styles.tileLabel, { color: c.textSecondary }]}>
                {label}
              </Text>
            </View>
          )
        })}
      </View>

      {extras.length > 0 && (
        <View style={styles.extras}>
          {extras.map((e) => (
            <Text key={e.key} style={[styles.extraLine, { color: c.textSecondary }]}>
              {e.label}: {resources[e.key]}
            </Text>
          ))}
        </View>
      )}

      {allUnknown && (
        <Text style={[styles.note, { color: c.textSecondary }]}>
          Sem informação de meios divulgada pela ANEPC.
        </Text>
      )}
    </Section>
  )
}

const styles = StyleSheet.create({
  tiles: {
    flexDirection: 'row',
    gap: Spacing.two,
  },
  tile: {
    flex: 1,
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.one,
  },
  tileValue: {
    fontSize: 26,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
  tileLabel: {
    fontSize: 12,
  },
  extras: {
    gap: Spacing.half,
  },
  extraLine: {
    fontSize: 13,
  },
  note: {
    fontSize: 12,
  },
})
