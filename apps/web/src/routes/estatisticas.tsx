import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import {
  Activity,
  CalendarDays,
  Flame,
  Loader2,
  TrendingUp,
} from 'lucide-react'

import { seasonStatsQuery } from '#/lib/fogos/api.ts'
import {
  formatInteger,
  formatPercent,
} from '#/lib/fogos/stats.ts'
import { formatDuration, formatHectares } from '#/lib/fogos/format.ts'
import type { SeasonStats } from '#/lib/fogos/types.ts'
import { PageHeader } from '#/components/page-header.tsx'
import { StatTile } from '#/components/stat-tile.tsx'
import {
  BurnAreaChart,
  CauseBreakdownChart,
  HourlyIgnitionsChart,
  IgnitionsYoYChart,
} from '#/components/season-charts.tsx'

const CURRENT_YEAR = new Date().getFullYear()

export const Route = createFileRoute('/estatisticas')({
  component: Estatisticas,
  loader: ({ context }) =>
    context.queryClient
      .ensureQueryData(seasonStatsQuery(CURRENT_YEAR))
      .catch(() => null),
})

function SectionCard({
  title,
  subtitle,
  children,
}: {
  title: string
  subtitle: string
  children: React.ReactNode
}) {
  return (
    <section className="rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60 sm:p-5">
      <h2 className="text-base font-semibold text-foreground">{title}</h2>
      <p className="mt-0.5 text-sm text-muted-foreground">{subtitle}</p>
      <div className="mt-4">{children}</div>
    </section>
  )
}

function Estatisticas() {
  const { data, isLoading, isError } = useQuery(seasonStatsQuery(CURRENT_YEAR))

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-6xl px-4 py-6">
        <div className="mb-6">
          <h1 className="text-2xl font-bold text-foreground">
            Estatísticas da época {CURRENT_YEAR}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Panorama nacional de ignições, área ardida, causas e tempos de
            resposta.
          </p>
        </div>

        {isLoading && !data ? (
          <div className="flex h-64 items-center justify-center">
            <Loader2 className="size-6 animate-spin text-muted-foreground" />
          </div>
        ) : isError && !data ? (
          <div className="rounded-2xl border border-black/5 bg-white/70 px-4 py-8 text-center text-sm text-muted-foreground dark:border-white/10 dark:bg-zinc-900/60">
            Não foi possível carregar as estatísticas. A tentar novamente…
          </div>
        ) : data ? (
          <Dashboard stats={data} />
        ) : null}
      </main>
    </div>
  )
}

function Dashboard({ stats }: { stats: SeasonStats }) {
  const { header } = stats
  return (
    <div className="space-y-6">
      {/* Header tiles */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <StatTile
          label="Incêndios ativos"
          value={formatInteger(header.activeFires)}
          Icon={Flame}
        />
        <StatTile
          label="Ignições hoje"
          value={formatInteger(header.today)}
          Icon={Activity}
        />
        <StatTile
          label="Ignições ontem"
          value={formatInteger(header.yesterday)}
          Icon={CalendarDays}
        />
        <StatTile
          label="Últimos 7 dias"
          value={formatInteger(header.week)}
          Icon={TrendingUp}
        />
        <StatTile
          label="Área ardida no ano"
          value={
            header.burnAreaTotalHa != null
              ? formatHectares(header.burnAreaTotalHa)
              : '—'
          }
          Icon={Flame}
        />
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <SectionCard
          title="Ignições acumuladas"
          subtitle={`Total de fogos ao longo do ano, ${stats.year} vs ${stats.year - 1}.`}
        >
          <IgnitionsYoYChart
            current={stats.ignitionsCurrent}
            previous={stats.ignitionsPrevious}
            year={stats.year}
          />
        </SectionCard>

        <SectionCard
          title="Área ardida acumulada"
          subtitle="Hectares contabilizados pelo ICNF ao longo do ano."
        >
          <BurnAreaChart series={stats.burnAreaCumulative} />
        </SectionCard>

        <SectionCard
          title="Causas dos incêndios"
          subtitle="Distribuição por família de causa (ICNF), por número de ocorrências."
        >
          <CauseBreakdownChart causes={stats.causeBreakdown} />
        </SectionCard>

        <SectionCard
          title="Padrão horário das ignições"
          subtitle="Ignições por hora do dia (hoje)."
        >
          <HourlyIgnitionsChart hours={stats.ignitionsHourly} />
        </SectionCard>
      </div>

      <SectionCard
        title="Falsos alarmes por distrito"
        subtitle="Percentagem de ocorrências classificadas como falso alarme ou alerta (mín. 20 ocorrências)."
      >
        <FalseAlarmTable stats={stats} />
      </SectionCard>

      <SectionCard
        title="Tempos de resposta"
        subtitle="Medianas nacionais das transições de estado das ocorrências no ano."
      >
        <ResponseTimes stats={stats} />
      </SectionCard>
    </div>
  )
}

function FalseAlarmTable({ stats }: { stats: SeasonStats }) {
  if (stats.falseAlarmStats.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Sem dados suficientes para o período.
      </p>
    )
  }
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-border/60 text-left text-xs uppercase tracking-wider text-muted-foreground">
            <th className="py-2 pr-4 font-medium">Distrito</th>
            <th className="py-2 pr-4 text-right font-medium">Total</th>
            <th className="py-2 pr-4 text-right font-medium">Falsos</th>
            <th className="py-2 text-right font-medium">Taxa</th>
          </tr>
        </thead>
        <tbody>
          {stats.falseAlarmStats.map((row) => (
            <tr
              key={row.district}
              className="border-b border-border/40 last:border-0"
            >
              <td className="py-2 pr-4 font-medium text-foreground">
                {row.district}
              </td>
              <td className="py-2 pr-4 text-right tabular-nums text-muted-foreground">
                {formatInteger(row.total)}
              </td>
              <td className="py-2 pr-4 text-right tabular-nums text-muted-foreground">
                {formatInteger(row.falseAlarms)}
              </td>
              <td className="py-2 text-right font-semibold tabular-nums text-foreground">
                {formatPercent(row.rate)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ResponseTimes({ stats }: { stats: SeasonStats }) {
  const rt = stats.responseTimeStats
  if (!rt || rt.count === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Sem dados de tempos de resposta para o período.
      </p>
    )
  }
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <StatTile
        label="Despacho → Chegada"
        value={
          rt.medianDispatchToArrivalSeconds != null
            ? formatDuration(rt.medianDispatchToArrivalSeconds)
            : '—'
        }
        hint="Mediana"
      />
      <StatTile
        label="Chegada → Resolução"
        value={
          rt.medianArrivalToControlSeconds != null
            ? formatDuration(rt.medianArrivalToControlSeconds)
            : '—'
        }
        hint="Mediana"
      />
      <StatTile
        label="Ocorrências analisadas"
        value={formatInteger(rt.count)}
      />
    </div>
  )
}
