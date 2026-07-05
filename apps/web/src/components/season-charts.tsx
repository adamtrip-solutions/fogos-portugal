import { useMemo } from 'react'
import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
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
import {
  alignYoY,
  dayOfYearLabel,
  formatInteger,
  hourLabel,
} from '#/lib/fogos/stats.ts'
import { formatHectares } from '#/lib/fogos/format.ts'
import type {
  CauseCount,
  DayArea,
  DayCount,
  HourBucket,
} from '#/lib/fogos/types.ts'

// ── Cumulative ignitions, current vs previous year ───────────────────────────

export function IgnitionsYoYChart({
  current,
  previous,
  year,
}: {
  current: DayCount[]
  previous: DayCount[]
  year: number
}) {
  const data = useMemo(() => alignYoY(current, previous), [current, previous])

  const config = {
    current: { label: String(year), color: 'var(--chart-1)' },
    previous: { label: String(year - 1), color: 'var(--chart-2)' },
  } satisfies ChartConfig

  if (data.length === 0) return <ChartEmpty />

  return (
    <ChartContainer config={config} className="aspect-auto h-56 w-full">
      <LineChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
        <CartesianGrid vertical={false} className="stroke-muted" />
        <XAxis
          dataKey="day"
          type="number"
          domain={[1, 'dataMax']}
          tickLine={false}
          axisLine={false}
          tickMargin={8}
          minTickGap={40}
          tickFormatter={(v: number) => dayOfYearLabel(v)}
        />
        <YAxis
          width={40}
          tickLine={false}
          axisLine={false}
          tickFormatter={(v: number) => formatInteger(v)}
        />
        <ChartTooltip
          content={
            <ChartTooltipContent
              labelFormatter={(_, payload) => {
                const day = payload?.[0]?.payload?.day as number | undefined
                return day != null ? dayOfYearLabel(day) : ''
              }}
            />
          }
        />
        <Line
          dataKey="previous"
          type="monotone"
          stroke="var(--color-previous)"
          strokeWidth={2}
          strokeDasharray="4 4"
          dot={false}
          connectNulls
        />
        <Line
          dataKey="current"
          type="monotone"
          stroke="var(--color-current)"
          strokeWidth={2}
          dot={false}
          connectNulls
        />
        <ChartLegend content={<ChartLegendContent />} />
      </LineChart>
    </ChartContainer>
  )
}

// ── Cumulative burn area ─────────────────────────────────────────────────────

export function BurnAreaChart({ series }: { series: DayArea[] }) {
  const data = useMemo(
    () => series.map((d) => ({ ts: Date.parse(d.date), totalHa: d.totalHa })),
    [series],
  )

  const config = {
    totalHa: { label: 'Área ardida', color: 'var(--chart-5)' },
  } satisfies ChartConfig

  const dateFmt = useMemo(
    () => new Intl.DateTimeFormat('pt-PT', { day: 'numeric', month: 'short' }),
    [],
  )

  if (data.length === 0) return <ChartEmpty />

  return (
    <ChartContainer config={config} className="aspect-auto h-56 w-full">
      <AreaChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
        <defs>
          <linearGradient id="burn-area-fill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="var(--color-totalHa)" stopOpacity={0.35} />
            <stop offset="100%" stopColor="var(--color-totalHa)" stopOpacity={0.03} />
          </linearGradient>
        </defs>
        <CartesianGrid vertical={false} className="stroke-muted" />
        <XAxis
          dataKey="ts"
          type="number"
          scale="time"
          domain={['dataMin', 'dataMax']}
          tickLine={false}
          axisLine={false}
          tickMargin={8}
          minTickGap={40}
          tickFormatter={(v: number) => dateFmt.format(v)}
        />
        <YAxis
          width={48}
          tickLine={false}
          axisLine={false}
          tickFormatter={(v: number) => formatInteger(v)}
        />
        <ChartTooltip
          content={
            <ChartTooltipContent
              labelFormatter={(_, payload) => {
                const ts = payload?.[0]?.payload?.ts as number | undefined
                return ts != null ? dateFmt.format(ts) : ''
              }}
              formatter={(value) => (
                <span className="font-mono font-medium tabular-nums text-foreground">
                  {formatHectares(Number(value))}
                </span>
              )}
            />
          }
        />
        <Area
          dataKey="totalHa"
          type="monotone"
          stroke="var(--color-totalHa)"
          strokeWidth={2}
          fill="url(#burn-area-fill)"
        />
      </AreaChart>
    </ChartContainer>
  )
}

// ── Cause breakdown (horizontal bars) ────────────────────────────────────────

const CAUSE_COLORS = [
  'var(--chart-1)',
  'var(--chart-2)',
  'var(--chart-3)',
  'var(--chart-4)',
  'var(--chart-5)',
]

export function CauseBreakdownChart({ causes }: { causes: CauseCount[] }) {
  const data = useMemo(() => causes.slice(0, 8), [causes])

  const config = { count: { label: 'Ocorrências' } } satisfies ChartConfig

  if (data.length === 0) return <ChartEmpty />

  return (
    <ChartContainer
      config={config}
      className="aspect-auto w-full"
      style={{ height: Math.max(120, data.length * 36 + 16) }}
    >
      <BarChart
        data={data}
        layout="vertical"
        margin={{ top: 0, right: 12, bottom: 0, left: 0 }}
      >
        <XAxis type="number" hide />
        <YAxis
          type="category"
          dataKey="causeFamily"
          width={120}
          tickLine={false}
          axisLine={false}
          tick={{ fontSize: 12 }}
        />
        <ChartTooltip
          cursor={false}
          content={
            <ChartTooltipContent
              formatter={(value, _name, item) => (
                <div className="flex flex-col gap-0.5">
                  <span className="font-mono font-medium tabular-nums text-foreground">
                    {formatInteger(Number(value))} ocorrências
                  </span>
                  <span className="text-muted-foreground">
                    {formatHectares(
                      (item?.payload as CauseCount | undefined)?.burnAreaHa ?? 0,
                    )}
                  </span>
                </div>
              )}
            />
          }
        />
        <Bar dataKey="count" radius={[0, 6, 6, 0]}>
          {data.map((_, i) => (
            <Cell key={i} fill={CAUSE_COLORS[i % CAUSE_COLORS.length]} />
          ))}
        </Bar>
      </BarChart>
    </ChartContainer>
  )
}

// ── Hourly ignition pattern (24 bars) ────────────────────────────────────────

export function HourlyIgnitionsChart({ hours }: { hours: HourBucket[] }) {
  const config = {
    count: { label: 'Ignições', color: 'var(--chart-1)' },
  } satisfies ChartConfig

  if (hours.length === 0) return <ChartEmpty />

  return (
    <ChartContainer config={config} className="aspect-auto h-48 w-full">
      <BarChart data={hours} margin={{ top: 4, right: 8, bottom: 0, left: 0 }}>
        <CartesianGrid vertical={false} className="stroke-muted" />
        <XAxis
          dataKey="hour"
          tickLine={false}
          axisLine={false}
          tickMargin={8}
          interval={2}
          tickFormatter={(v: number) => hourLabel(v)}
        />
        <YAxis
          width={32}
          tickLine={false}
          axisLine={false}
          tickFormatter={(v: number) => formatInteger(v)}
        />
        <ChartTooltip
          cursor={false}
          content={
            <ChartTooltipContent
              labelFormatter={(_, payload) => {
                const h = payload?.[0]?.payload?.hour as number | undefined
                return h != null ? hourLabel(h) : ''
              }}
            />
          }
        />
        <Bar dataKey="count" fill="var(--color-count)" radius={[4, 4, 0, 0]} />
      </BarChart>
    </ChartContainer>
  )
}

function ChartEmpty() {
  return (
    <div className="flex h-40 items-center justify-center rounded-xl bg-muted/40 text-sm text-muted-foreground">
      Sem dados para o período.
    </div>
  )
}
