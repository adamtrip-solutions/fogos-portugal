import { useMemo } from 'react'
import {
  CartesianGrid,
  Line,
  LineChart,
  XAxis,
  YAxis,
} from 'recharts'

import {
  ChartContainer,
  ChartLegend,
  ChartLegendContent,
  ChartTooltip,
  ChartTooltipContent,
} from '#/components/ui/chart.tsx'
import type { ChartConfig } from '#/components/ui/chart.tsx'
import type { ResourceSnapshot } from '#/lib/fogos/types.ts'

const chartConfig = {
  man: { label: 'Operacionais', color: '#FF6E02' },
  terrain: { label: 'Terrestres', color: '#B81E1F' },
  aerial: { label: 'Aéreos', color: '#2563EB' },
} satisfies ChartConfig

interface Point {
  ts: number
  man: number | null
  terrain: number | null
  aerial: number | null
}

/** `-1` is the unknown sentinel — turn it into a null gap. */
function usable(value: number): number | null {
  return value >= 0 ? value : null
}

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

export function ResourceChart({
  history,
  current,
  startedAt,
}: {
  history: ResourceSnapshot[]
  /** Latest known values (stamped with the incident's `updatedAt`) — extends the series to the last update. */
  current?: ResourceSnapshot
  /** The incident's `occurredAt` (alert time) — anchors the series at zero. */
  startedAt?: string
}) {
  const points = useMemo<Point[]>(() => {
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
    // `current` can coincide with the newest snapshot — keep one point per instant.
    const deduped = sorted.filter((p, i) => i === 0 || p.ts !== sorted[i - 1].ts)
    // Anchor the series at zero on the alert instant: we only start observing a
    // fire once ANEPC surfaces it, so the first snapshot lands mid-ramp (e.g.
    // 43 operacionais out of nowhere). A synthetic 0/0/0 at occurredAt shows
    // the ramp-up. Chart-only — the stored history and the public API carry
    // real observations exclusively (a stored zero would also read as an
    // explosive delta to the escalation detector).
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

  if (points.length === 0) return null

  // Young incidents have few snapshots; mark them so a short series still reads.
  const sparse = points.length <= 8

  const first = points[0].ts
  const last = points[points.length - 1].ts
  const spansMultiDay = last - first > DAY_MS
  const formatStamp = (ts: number) =>
    spansMultiDay ? dayTimeFmt.format(ts) : timeFmt.format(ts)

  return (
    <section className="space-y-2">
      <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
        Evolução dos meios
      </h3>
      <ChartContainer config={chartConfig} className="aspect-auto h-40 w-full">
        <LineChart data={points} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid vertical={false} className="stroke-muted" />
          <XAxis
            dataKey="ts"
            type="number"
            scale="time"
            domain={['dataMin', 'dataMax']}
            tickLine={false}
            axisLine={false}
            tickMargin={8}
            minTickGap={32}
            tickCount={4}
            tickFormatter={(value: number) => formatStamp(value)}
          />
          <YAxis hide domain={[0, 'auto']} />
          <ChartTooltip
            content={
              <ChartTooltipContent
                labelFormatter={(_, payload) => {
                  const ts = payload?.[0]?.payload?.ts as number | undefined
                  return ts != null ? formatStamp(ts) : ''
                }}
              />
            }
          />
          <Line
            dataKey="man"
            type="monotone"
            stroke="var(--color-man)"
            strokeWidth={2}
            dot={sparse ? { r: 2.5, fill: 'var(--color-man)', strokeWidth: 0 } : false}
            connectNulls
          />
          <Line
            dataKey="terrain"
            type="monotone"
            stroke="var(--color-terrain)"
            strokeWidth={2}
            dot={sparse ? { r: 2.5, fill: 'var(--color-terrain)', strokeWidth: 0 } : false}
            connectNulls
          />
          <Line
            dataKey="aerial"
            type="monotone"
            stroke="var(--color-aerial)"
            strokeWidth={2}
            dot={sparse ? { r: 2.5, fill: 'var(--color-aerial)', strokeWidth: 0 } : false}
            connectNulls
          />
          <ChartLegend content={<ChartLegendContent />} />
        </LineChart>
      </ChartContainer>
    </section>
  )
}
