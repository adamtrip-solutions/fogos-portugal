import { useMemo } from 'react'
import { StyleSheet, Text, View } from 'react-native'
import { Area, Bar, CartesianChart, Line } from 'victory-native'

import type { CauseCount, DayArea, DayCount, HourBucket } from '@fogos/api-client'
import { alignYoY } from '@fogos/api-client'
import {
  dayOfYearLabel,
  formatHectares,
  formatInteger,
  hourLabel,
} from '@fogos/ui-tokens'

import { Spacing } from '@/constants/theme'
import type { ThemeColors } from '@/components/incident/section'

// Series palette ported from web's season-charts.tsx (shadcn --chart-1..5),
// converted from oklch to hex per scheme so lines/bars stay legible on both
// black and white. Web splits the palette light/dark; mobile keeps that split.
const PALETTE = {
  light: ['#f54900', '#009689', '#104e64', '#ffb900', '#fe9a00'],
  dark: ['#1447e6', '#00bc7d', '#fe9a00', '#ad46ff', '#ff2056'],
} as const

/** Reads the scheme off the theme bag (black text ⇒ light, white text ⇒ dark). */
function palette(c: ThemeColors): readonly string[] {
  return c.text === '#ffffff' ? PALETTE.dark : PALETTE.light
}

const CHART_HEIGHT = 168

// ── shared chrome ────────────────────────────────────────────────────────────

function ChartEmpty({ c }: { c: ThemeColors }) {
  return (
    <View style={[styles.empty, { backgroundColor: c.backgroundSelected }]}>
      <Text style={[styles.emptyText, { color: c.textSecondary }]}>
        Sem dados para o período.
      </Text>
    </View>
  )
}

/** A start … end x-axis label pair rendered as RN text (no Skia font). */
function AxisRange({
  start,
  end,
  c,
}: {
  start: string
  end: string
  c: ThemeColors
}) {
  return (
    <View style={styles.axis}>
      <Text style={[styles.axisLabel, { color: c.textSecondary }]}>{start}</Text>
      <Text style={[styles.axisLabel, { color: c.textSecondary }]}>{end}</Text>
    </View>
  )
}

/** Evenly spaced x tick labels rendered as RN text (space-between alignment). */
function AxisTicks({ ticks, c }: { ticks: string[]; c: ThemeColors }) {
  return (
    <View style={styles.axis}>
      {ticks.map((t, i) => (
        <Text key={i} style={[styles.axisLabel, { color: c.textSecondary }]}>
          {t}
        </Text>
      ))}
    </View>
  )
}

function Legend({
  items,
  c,
}: {
  items: { label: string; color: string }[]
  c: ThemeColors
}) {
  return (
    <View style={styles.legend}>
      {items.map((s) => (
        <View key={s.label} style={styles.legendItem}>
          <View style={[styles.legendDot, { backgroundColor: s.color }]} />
          <Text style={[styles.legendLabel, { color: c.textSecondary }]}>
            {s.label}
          </Text>
        </View>
      ))}
    </View>
  )
}

/** Muted "peak N" reference so a Skia-font-free chart still carries a y scale. */
function PeakLabel({ text, c }: { text: string; c: ThemeColors }) {
  return <Text style={[styles.peak, { color: c.textSecondary }]}>{text}</Text>
}

// ── Cumulative ignitions, current vs previous year ───────────────────────────

type YoYDatum = { day: number; current: number | null; previous: number | null }

export function IgnitionsYoYChart({
  current,
  previous,
  year,
  c,
}: {
  current: DayCount[]
  previous: DayCount[]
  year: number
  c: ThemeColors
}) {
  const data = useMemo<YoYDatum[]>(
    () =>
      alignYoY(current, previous).map((p) => ({
        day: p.day,
        current: p.current,
        previous: p.previous,
      })),
    [current, previous],
  )

  if (data.length === 0) return <ChartEmpty c={c} />

  const pal = palette(c)
  const currentColor = pal[0]
  const previousColor = pal[1]
  const peak = Math.max(
    0,
    ...data.map((d) => Math.max(d.current ?? 0, d.previous ?? 0)),
  )

  return (
    <View>
      <PeakLabel text={`Máx. ${formatInteger(peak)}`} c={c} />
      <View style={styles.plot}>
        <CartesianChart
          data={data}
          xKey="day"
          yKeys={['current', 'previous']}
          domain={{ y: [0] }}
          domainPadding={{ top: 12, bottom: 2 }}
          padding={{ left: 2, right: 2, top: 2, bottom: 2 }}
        >
          {({ points }) => (
            <>
              <Line
                points={points.previous}
                color={previousColor}
                strokeWidth={2}
                curveType="monotoneX"
                connectMissingData
              />
              <Line
                points={points.current}
                color={currentColor}
                strokeWidth={2}
                curveType="monotoneX"
                connectMissingData
              />
            </>
          )}
        </CartesianChart>
      </View>
      <AxisRange
        start={dayOfYearLabel(data[0].day)}
        end={dayOfYearLabel(data[data.length - 1].day)}
        c={c}
      />
      <Legend
        items={[
          { label: String(year), color: currentColor },
          { label: String(year - 1), color: previousColor },
        ]}
        c={c}
      />
    </View>
  )
}

// ── Cumulative burn area (area chart) ────────────────────────────────────────

type AreaDatum = { ts: number; totalHa: number }

const dateFmt = new Intl.DateTimeFormat('pt-PT', { day: 'numeric', month: 'short' })

export function BurnAreaChart({ series, c }: { series: DayArea[]; c: ThemeColors }) {
  const data = useMemo<AreaDatum[]>(
    () => series.map((d) => ({ ts: Date.parse(d.date), totalHa: d.totalHa })),
    [series],
  )

  if (data.length === 0) return <ChartEmpty c={c} />

  const color = palette(c)[4]
  const latest = data[data.length - 1].totalHa

  return (
    <View>
      <PeakLabel text={`Total ${formatHectares(latest)}`} c={c} />
      <View style={styles.plot}>
        <CartesianChart
          data={data}
          xKey="ts"
          yKeys={['totalHa']}
          domain={{ y: [0] }}
          domainPadding={{ top: 12, bottom: 2 }}
          padding={{ left: 2, right: 2, top: 2, bottom: 2 }}
        >
          {({ points, chartBounds }) => (
            <>
              <Area
                points={points.totalHa}
                y0={chartBounds.bottom}
                color={color}
                opacity={0.22}
                curveType="monotoneX"
                connectMissingData
              />
              <Line
                points={points.totalHa}
                color={color}
                strokeWidth={2}
                curveType="monotoneX"
                connectMissingData
              />
            </>
          )}
        </CartesianChart>
      </View>
      <AxisRange
        start={dateFmt.format(data[0].ts)}
        end={dateFmt.format(data[data.length - 1].ts)}
        c={c}
      />
    </View>
  )
}

// ── Cause breakdown (manual horizontal bars) ─────────────────────────────────
// Web draws vertical-layout (horizontal) bars via recharts. On mobile a plain RN
// bar list reads cleaner and keeps every cause label fully legible without
// fighting a Skia category axis — per the plan's "your call" on this chart.

export function CauseBreakdownChart({
  causes,
  c,
}: {
  causes: CauseCount[]
  c: ThemeColors
}) {
  const data = useMemo(() => causes.slice(0, 8), [causes])
  if (data.length === 0) return <ChartEmpty c={c} />

  const pal = palette(c)
  const max = Math.max(1, ...data.map((d) => d.count))

  return (
    <View style={styles.causes}>
      {data.map((cause, i) => (
        <View key={cause.causeFamily} style={styles.causeRow}>
          <View style={styles.causeHead}>
            <Text
              style={[styles.causeLabel, { color: c.text }]}
              numberOfLines={1}
            >
              {cause.causeFamily}
            </Text>
            <Text style={[styles.causeCount, { color: c.textSecondary }]}>
              {formatInteger(cause.count)} · {formatHectares(cause.burnAreaHa)}
            </Text>
          </View>
          <View style={[styles.track, { backgroundColor: c.backgroundSelected }]}>
            <View
              style={[
                styles.fill,
                {
                  backgroundColor: pal[i % pal.length],
                  width: `${Math.max(2, (cause.count / max) * 100)}%`,
                },
              ]}
            />
          </View>
        </View>
      ))}
    </View>
  )
}

// ── Hourly ignition pattern (24 bars) ────────────────────────────────────────

type HourDatum = { hour: number; count: number }

export function HourlyIgnitionsChart({
  hours,
  c,
}: {
  hours: HourBucket[]
  c: ThemeColors
}) {
  const data = useMemo<HourDatum[]>(
    () => hours.map((h) => ({ hour: h.hour, count: h.count })),
    [hours],
  )
  if (data.length === 0) return <ChartEmpty c={c} />

  const color = palette(c)[0]
  const peak = Math.max(0, ...data.map((d) => d.count))

  return (
    <View>
      <PeakLabel text={`Máx. ${formatInteger(peak)}`} c={c} />
      <View style={styles.plot}>
        <CartesianChart
          data={data}
          xKey="hour"
          yKeys={['count']}
          domain={{ y: [0] }}
          domainPadding={{ top: 12, bottom: 2, left: 6, right: 6 }}
          padding={{ left: 2, right: 2, top: 2, bottom: 2 }}
        >
          {({ points, chartBounds }) => (
            <Bar
              points={points.count}
              chartBounds={chartBounds}
              color={color}
              innerPadding={0.3}
              roundedCorners={{ topLeft: 3, topRight: 3 }}
            />
          )}
        </CartesianChart>
      </View>
      <AxisTicks
        ticks={[0, 6, 12, 18, 23].map((h) => hourLabel(h))}
        c={c}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  plot: {
    height: CHART_HEIGHT,
  },
  empty: {
    height: 120,
    borderRadius: Spacing.two,
    alignItems: 'center',
    justifyContent: 'center',
  },
  emptyText: {
    fontSize: 13,
  },
  peak: {
    fontSize: 11,
    fontWeight: '600',
    fontVariant: ['tabular-nums'],
    marginBottom: Spacing.one,
  },
  axis: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    marginTop: Spacing.one,
  },
  axisLabel: {
    fontSize: 11,
    fontVariant: ['tabular-nums'],
  },
  legend: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.three,
    marginTop: Spacing.two,
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
  causes: {
    gap: Spacing.two,
  },
  causeRow: {
    gap: Spacing.one,
  },
  causeHead: {
    flexDirection: 'row',
    alignItems: 'baseline',
    justifyContent: 'space-between',
    gap: Spacing.two,
  },
  causeLabel: {
    flex: 1,
    fontSize: 13,
    fontWeight: '500',
  },
  causeCount: {
    fontSize: 12,
    fontVariant: ['tabular-nums'],
  },
  track: {
    height: 8,
    borderRadius: 999,
    overflow: 'hidden',
  },
  fill: {
    height: 8,
    borderRadius: 999,
  },
})
