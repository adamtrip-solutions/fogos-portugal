import { useMemo } from 'react'
import { StyleSheet, Text, View } from 'react-native'
import { CartesianChart, Line } from 'victory-native'

import type { ResourceSnapshot } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

// Series colors + labels ported verbatim from web's resource-chart.tsx
// chartConfig. Web uses one color per series across both light/dark schemes
// (single CSS var, not scheme-split); all three read on white and black.
const SERIES = [
  { key: 'man', label: 'Operacionais', color: '#FF6E02' },
  { key: 'terrain', label: 'Terrestres', color: '#B81E1F' },
  { key: 'aerial', label: 'Aéreos', color: '#2563EB' },
] as const

const DAY_MS = 24 * 60 * 60 * 1000

const timeFmt = new Intl.DateTimeFormat('pt-PT', {
  hour: '2-digit',
  minute: '2-digit',
})
const dayTimeFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

// A type alias (not an interface) so it satisfies victory-native's
// `RawData extends Record<string, unknown>` constraint.
type ChartDatum = {
  ts: number
  man: number | null
  terrain: number | null
  aerial: number | null
}

/** `-1` is the unknown sentinel — turn it into a null gap (web parity). */
function usable(value: number): number | null {
  return value >= 0 ? value : null
}

/**
 * Compact resource-history line chart (victory-native / Skia) — the mobile port
 * of web's `ResourceChart`. Series colors + the synthetic zero-anchor at the
 * alert instant match web. No interactivity in v1 (no cursor/tooltip). Renders
 * nothing when fewer than two points survive (web guards on 0; the sheet asks
 * for < 2 so a lone dot never shows an empty frame).
 */
export function ResourceChart({
  history,
  current,
  startedAt,
  c,
}: {
  history: ResourceSnapshot[]
  /** Latest values stamped with the incident's updatedAt — extends the series. */
  current?: ResourceSnapshot
  /** The incident's occurredAt (alert time) — anchors the series at zero. */
  startedAt?: string
  c: ThemeColors
}) {
  const points = useMemo<ChartDatum[]>(() => {
    const sorted = (current ? [...history, current] : history)
      .map((s) => ({
        ts: Date.parse(s.at),
        man: usable(s.man),
        terrain: usable(s.terrain),
        aerial: usable(s.aerial),
      }))
      // Drop snapshots where all three readings are unknown.
      .filter((p) => p.man != null || p.terrain != null || p.aerial != null)
      .sort((a, b) => a.ts - b.ts)
    // Keep one point per instant (current can coincide with the newest snapshot).
    const deduped = sorted.filter((p, i) => i === 0 || p.ts !== sorted[i - 1].ts)
    // Anchor at 0/0/0 on the alert instant: a fire is only surfaced mid-ramp, so
    // the first snapshot lands with crews already committed. Chart-only.
    const start = startedAt ? Date.parse(startedAt) : Number.NaN
    const first = deduped[0]
    if (
      first &&
      Number.isFinite(start) &&
      start < first.ts &&
      ((first.man ?? 0) > 0 || (first.terrain ?? 0) > 0 || (first.aerial ?? 0) > 0)
    ) {
      return [{ ts: start, man: 0, terrain: 0, aerial: 0 }, ...deduped]
    }
    return deduped
  }, [history, current, startedAt])

  if (points.length < 2) return null

  const first = points[0].ts
  const last = points[points.length - 1].ts
  const spansMultiDay = last - first > DAY_MS
  const fmt = (ts: number) =>
    spansMultiDay ? dayTimeFmt.format(ts) : timeFmt.format(ts)

  return (
    <Section title="Evolução dos meios" c={c}>
      <View style={styles.chart}>
        <CartesianChart
          data={points}
          xKey="ts"
          yKeys={['man', 'terrain', 'aerial']}
          domain={{ y: [0] }}
          domainPadding={{ top: 10, bottom: 2 }}
          padding={{ left: 2, right: 2, top: 2, bottom: 2 }}
        >
          {({ points: p }) => (
            <>
              <Line
                points={p.man}
                color={SERIES[0].color}
                strokeWidth={2}
                curveType="monotoneX"
                connectMissingData
              />
              <Line
                points={p.terrain}
                color={SERIES[1].color}
                strokeWidth={2}
                curveType="monotoneX"
                connectMissingData
              />
              <Line
                points={p.aerial}
                color={SERIES[2].color}
                strokeWidth={2}
                curveType="monotoneX"
                connectMissingData
              />
            </>
          )}
        </CartesianChart>
      </View>

      <View style={styles.axis}>
        <Text style={[styles.axisLabel, { color: c.textSecondary }]}>
          {fmt(first)}
        </Text>
        <Text style={[styles.axisLabel, { color: c.textSecondary }]}>
          {fmt(last)}
        </Text>
      </View>

      <View style={styles.legend}>
        {SERIES.map((s) => (
          <View key={s.key} style={styles.legendItem}>
            <View style={[styles.legendDot, { backgroundColor: s.color }]} />
            <Text style={[styles.legendLabel, { color: c.textSecondary }]}>
              {s.label}
            </Text>
          </View>
        ))}
      </View>
    </Section>
  )
}

const styles = StyleSheet.create({
  chart: {
    height: 176,
  },
  axis: {
    flexDirection: 'row',
    justifyContent: 'space-between',
  },
  axisLabel: {
    fontSize: 11,
    fontVariant: ['tabular-nums'],
  },
  legend: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.three,
    marginTop: Spacing.half,
  },
  legendItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.one + 2,
  },
  legendDot: {
    width: 10,
    height: 10,
    borderRadius: 999,
  },
  legendLabel: {
    fontSize: 12,
  },
})
