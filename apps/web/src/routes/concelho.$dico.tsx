import { createFileRoute, Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Loader2, MapPin, TriangleAlert } from 'lucide-react'

import { concelhoProfileQuery } from '#/lib/fogos/api.ts'
import { formatInteger, formatSignedPercent, yoyRatio } from '#/lib/fogos/stats.ts'
import { formatHectares } from '#/lib/fogos/format.ts'
import type {
  ConcelhoProfile,
  ConcelhoRiskDay,
  WeatherWarning,
} from '#/lib/fogos/types.ts'
import { IncidentRow } from '#/components/incident-row.tsx'
import { PageHeader } from '#/components/page-header.tsx'
import { StatTile } from '#/components/stat-tile.tsx'

export const Route = createFileRoute('/concelho/$dico')({
  component: Concelho,
  loader: ({ context, params }) =>
    context.queryClient
      .ensureQueryData(concelhoProfileQuery(params.dico))
      .catch(() => null),
})

// Fire-risk level → color + PT-friendly semantics (IPMA 1–5 scale).
const RISK_STYLE: Record<number, { bg: string; label: string }> = {
  1: { bg: '#6ABF59', label: 'Reduzido' },
  2: { bg: '#C6D82F', label: 'Moderado' },
  3: { bg: '#F5B301', label: 'Elevado' },
  4: { bg: '#FF6E02', label: 'Muito elevado' },
  5: { bg: '#B81E1F', label: 'Máximo' },
}

function riskStyle(level: number) {
  return RISK_STYLE[level] ?? { bg: '#BDBDBD', label: '—' }
}

const WARNING_COLOR: Record<string, string> = {
  yellow: '#F5B301',
  orange: '#FF6E02',
  red: '#B81E1F',
}

function Concelho() {
  const { dico } = Route.useParams()
  const { data, isLoading, isError } = useQuery(concelhoProfileQuery(dico))

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-4xl px-4 py-6">
        {isLoading && !data ? (
          <div className="flex h-64 items-center justify-center">
            <Loader2 className="size-6 animate-spin text-muted-foreground" />
          </div>
        ) : !data ? (
          <div className="rounded-2xl border border-black/5 bg-white/70 px-4 py-10 text-center dark:border-white/10 dark:bg-zinc-900/60">
            <p className="text-sm text-muted-foreground">
              {isError
                ? 'Não foi possível carregar o concelho. A tentar novamente…'
                : 'Concelho desconhecido.'}
            </p>
            <Link
              to="/"
              viewTransition
              className="mt-3 inline-block text-sm font-medium text-orange-600 hover:underline dark:text-orange-400"
            >
              Voltar ao mapa
            </Link>
          </div>
        ) : (
          <Profile profile={data} />
        )}
      </main>
    </div>
  )
}

function Profile({ profile }: { profile: ConcelhoProfile }) {
  const ratio = yoyRatio(profile.yearIgnitions, profile.previousYearIgnitions)
  const delta =
    ratio == null
      ? undefined
      : {
          text: formatSignedPercent(ratio),
          tone:
            ratio > 0 ? ('up' as const) : ratio < 0 ? ('down' as const) : ('neutral' as const),
        }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <p className="text-sm font-medium text-muted-foreground">
          {profile.district}
        </p>
        <h1 className="text-2xl font-bold text-foreground">{profile.name}</h1>
      </div>

      {/* Risk strip */}
      {profile.risk.length > 0 && <RiskStrip risk={profile.risk} />}

      {/* YoY tiles */}
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <StatTile
          label={`Ignições em ${new Date().getFullYear()}`}
          value={formatInteger(profile.yearIgnitions)}
          hint={`${formatInteger(profile.previousYearIgnitions)} no ano anterior`}
          delta={delta}
        />
        <StatTile
          label="Ano anterior (mesmo período)"
          value={formatInteger(profile.previousYearIgnitions)}
        />
        <StatTile
          label="Área ardida no ano"
          value={formatHectares(profile.yearBurnAreaHa)}
        />
      </div>

      {/* Active incidents */}
      <section>
        <h2 className="mb-3 text-base font-semibold text-foreground">
          Ocorrências ativas
        </h2>
        {profile.activeIncidents.length === 0 ? (
          <p className="rounded-2xl border border-black/5 bg-white/70 px-4 py-6 text-center text-sm text-muted-foreground dark:border-white/10 dark:bg-zinc-900/60">
            Sem ocorrências ativas neste concelho.
          </p>
        ) : (
          <ul className="space-y-2">
            {profile.activeIncidents.map((inc) => (
              <IncidentRow key={inc.id} incident={inc} />
            ))}
          </ul>
        )}
      </section>

      {/* IPMA warnings */}
      {profile.weatherWarnings.length > 0 && (
        <section>
          <h2 className="mb-3 text-base font-semibold text-foreground">
            Avisos meteorológicos (IPMA)
          </h2>
          <div className="grid gap-3 sm:grid-cols-2">
            {profile.weatherWarnings.map((w) => (
              <WarningCard key={w.id} warning={w} />
            ))}
          </div>
        </section>
      )}
    </div>
  )
}

function RiskStrip({ risk }: { risk: ConcelhoRiskDay[] }) {
  const dayFmt = new Intl.DateTimeFormat('pt-PT', { weekday: 'short', day: 'numeric' })
  return (
    <section>
      <h2 className="mb-3 text-base font-semibold text-foreground">
        Risco de incêndio
      </h2>
      <div className="grid grid-cols-5 gap-2">
        {risk.slice(0, 5).map((r) => {
          const style = riskStyle(r.level)
          return (
            <div
              key={r.date}
              className="flex flex-col items-center gap-1.5 rounded-xl border border-black/5 bg-white/70 p-2 text-center dark:border-white/10 dark:bg-zinc-900/60"
            >
              <span className="text-[11px] font-medium text-muted-foreground">
                {dayFmt.format(new Date(`${r.date}T00:00:00`))}
              </span>
              <span
                className="flex size-9 items-center justify-center rounded-full text-sm font-bold text-white"
                style={{ backgroundColor: style.bg }}
                title={r.label}
              >
                {r.level}
              </span>
              <span className="text-[10px] leading-tight text-muted-foreground">
                {r.label || style.label}
              </span>
            </div>
          )
        })}
      </div>
    </section>
  )
}

function WarningCard({ warning }: { warning: WeatherWarning }) {
  const color = WARNING_COLOR[warning.level.toLowerCase()] ?? '#BDBDBD'
  const endFmt = new Intl.DateTimeFormat('pt-PT', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  })
  return (
    <div className="rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm dark:border-white/10 dark:bg-zinc-900/60">
      <div className="flex items-center gap-2">
        <span
          className="flex size-7 shrink-0 items-center justify-center rounded-lg"
          style={{ backgroundColor: `${color}22` }}
        >
          <TriangleAlert className="size-4" style={{ color }} aria-hidden />
        </span>
        <div className="min-w-0">
          <p className="truncate text-sm font-semibold text-foreground">
            {warning.awarenessType}
          </p>
          <p className="text-xs font-medium" style={{ color }}>
            Aviso {warning.levelPt}
          </p>
        </div>
      </div>
      {warning.text && (
        <p className="mt-2 line-clamp-3 text-xs text-muted-foreground">
          {warning.text}
        </p>
      )}
      <p className="mt-2 flex items-center gap-1 text-[11px] text-muted-foreground">
        <MapPin className="size-3" aria-hidden />
        Até {endFmt.format(new Date(warning.endsAt))}
      </p>
    </div>
  )
}
