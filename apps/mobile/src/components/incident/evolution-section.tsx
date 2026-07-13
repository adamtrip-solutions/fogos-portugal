import { StyleSheet, Text, View } from 'react-native'

import {
  buildStatusTimeline,
  formatDuration,
  formatTimelineStamp,
  statusColorForCode,
} from '@fogos/ui-tokens'
import type { ResponseTimes, StatusHistoryEntry } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

// The three response-time tiles web renders (controlToConclusion is fetched but
// not shown); labels copied verbatim from web's RESPONSE_TIME_TILES.
const RESPONSE_TILES = [
  { key: 'dispatchToArrivalSeconds', label: 'Despacho → Chegada' },
  { key: 'arrivalToControlSeconds', label: 'Chegada → Resolução' },
  { key: 'totalSeconds', label: 'Total' },
] as const

/** Muted dot for the synthetic "Alerta" origin (web uses muted-foreground/30). */
const ORIGIN_DOT = 'rgba(128,128,128,0.4)'

/**
 * "Evolução" — response-time tiles over the status timeline, ported from web's
 * `EvolutionSection`. The timeline runs newest → oldest and ends on the
 * synthetic "Alerta" origin (see `buildStatusTimeline`). Renders nothing when
 * there is neither history nor a response time.
 */
export function EvolutionSection({
  history,
  responseTimes,
  occurredAt,
  c,
}: {
  history: StatusHistoryEntry[]
  responseTimes: ResponseTimes | null
  occurredAt: string
  c: ThemeColors
}) {
  const tiles = RESPONSE_TILES.filter(
    (t) => responseTimes?.[t.key] != null,
  ).map((t) => ({ label: t.label, value: responseTimes![t.key] as number }))

  const rows = buildStatusTimeline(history, occurredAt)

  if (tiles.length === 0 && rows.length === 0) return null

  return (
    <Section title="Evolução" c={c}>
      {tiles.length > 0 && (
        <View style={styles.tiles}>
          {tiles.map((t) => (
            <View
              key={t.label}
              style={[styles.tile, { backgroundColor: c.backgroundElement }]}
            >
              <Text style={[styles.tileValue, { color: c.text }]}>
                {formatDuration(t.value)}
              </Text>
              <Text style={[styles.tileLabel, { color: c.textSecondary }]}>
                {t.label}
              </Text>
            </View>
          ))}
        </View>
      )}

      {rows.length > 0 && (
        <View style={styles.timeline}>
          {rows.map((r, i) => (
            <View key={`${r.at}-${i}`} style={styles.entry}>
              <View
                style={[
                  styles.dot,
                  { backgroundColor: r.synthetic ? ORIGIN_DOT : statusColorForCode(r.code) },
                ]}
              />
              <View style={styles.entryText}>
                <Text
                  style={[
                    styles.entryLabel,
                    { color: r.synthetic ? c.textSecondary : c.text },
                  ]}
                >
                  {r.label}
                </Text>
                <Text style={[styles.entryStamp, { color: c.textSecondary }]}>
                  {formatTimelineStamp(r.at)}
                </Text>
              </View>
            </View>
          ))}
        </View>
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
    gap: Spacing.half,
  },
  tileValue: {
    fontSize: 15,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
  tileLabel: {
    fontSize: 11,
    lineHeight: 14,
  },
  timeline: {
    gap: Spacing.three,
    marginTop: Spacing.one,
  },
  entry: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: Spacing.three,
  },
  dot: {
    width: 10,
    height: 10,
    borderRadius: 999,
    marginTop: 4,
  },
  entryText: {
    flex: 1,
    gap: 1,
  },
  entryLabel: {
    fontSize: 14,
    fontWeight: '600',
  },
  entryStamp: {
    fontSize: 12,
  },
})
