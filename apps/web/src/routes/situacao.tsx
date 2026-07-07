import { createFileRoute, Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { ChevronDown, Flame, Plane, Truck, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

import { recentIncidentsQuery, situationReportsQuery } from '#/lib/fogos/api.ts'
import {
  incidentTitle,
  locationParts,
  statusColorForCode,
} from '#/lib/fogos/format.ts'
import type { IncidentListItem, SituationReport } from '#/lib/fogos/types.ts'
import { pageMeta } from '#/lib/seo.ts'
import { PageHeader } from '#/components/page-header.tsx'
import { Skeleton } from '#/components/ui/skeleton.tsx'

export const Route = createFileRoute('/situacao')({
  head: () =>
    pageMeta({
      title: 'Situação atual — FogosPortugal',
      description:
        'Ponto de situação nacional dos incêndios em Portugal: fogos ativos, operacionais e meios no terreno, atualizado ao longo do dia com dados oficiais.',
      path: '/situacao',
    }),
  component: Situacao,
  loader: ({ context }) =>
    context.queryClient
      .ensureQueryData(situationReportsQuery(14))
      .catch(() => null),
})

// ── Styling tokens (shared with the rest of the app) ─────────────────────────

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60'

// ── Lisbon date formatting ───────────────────────────────────────────────────

const timeFmt = new Intl.DateTimeFormat('pt-PT', {
  timeZone: 'Europe/Lisbon',
  hour: '2-digit',
  minute: '2-digit',
})
const dayMonthFmt = new Intl.DateTimeFormat('pt-PT', {
  timeZone: 'Europe/Lisbon',
  day: 'numeric',
  month: 'short',
})

/** `morning` → "Manhã", anything else → "Noite" (mirrors the worker's slot). */
function slotLabel(slot: string): string {
  return slot === 'morning' ? 'Manhã' : 'Noite'
}

// ── Headline stats ───────────────────────────────────────────────────────────

const STAT_TILES: Array<{
  key: keyof Pick<
    SituationReport,
    'activeFires' | 'totalMan' | 'totalTerrain' | 'totalAerial'
  >
  label: string
  Icon: LucideIcon
}> = [
  { key: 'activeFires', label: 'Fogos ativos', Icon: Flame },
  { key: 'totalMan', label: 'Operacionais', Icon: Users },
  { key: 'totalTerrain', label: 'Meios terrestres', Icon: Truck },
  { key: 'totalAerial', label: 'Meios aéreos', Icon: Plane },
]

const numberFmt = new Intl.NumberFormat('pt-PT')

// ── Deltas vs the previous report ────────────────────────────────────────────

type DeltaTone = 'good' | 'bad' | 'neutral'

const DELTA_TONE: Record<DeltaTone, string> = {
  good: 'bg-emerald-500/12 text-emerald-700 ring-emerald-500/30 dark:text-emerald-300',
  bad: 'bg-red-500/12 text-red-700 ring-red-500/30 dark:text-red-300',
  neutral: 'bg-muted/60 text-muted-foreground ring-black/5 dark:ring-white/10',
}

interface DeltaChip {
  label: string
  tone: DeltaTone
}

/** A `+N`/`−N` chip when the value moved; null when unchanged. */
function makeDelta(
  curr: number,
  prev: number,
  noun: string,
  kind: 'fires' | 'resource',
): DeltaChip | null {
  const diff = curr - prev
  if (diff === 0) return null
  const sign = diff > 0 ? '+' : '−'
  const label = `${sign}${numberFmt.format(Math.abs(diff))} ${noun}`
  const tone: DeltaTone =
    kind === 'fires' ? (diff > 0 ? 'bad' : 'good') : 'neutral'
  return { label, tone }
}

function deltasFor(curr: SituationReport, prev: SituationReport): DeltaChip[] {
  return [
    makeDelta(curr.activeFires, prev.activeFires, 'fogos', 'fires'),
    makeDelta(curr.totalMan, prev.totalMan, 'operacionais', 'resource'),
    makeDelta(curr.totalTerrain, prev.totalTerrain, 'terrestres', 'resource'),
    makeDelta(curr.totalAerial, prev.totalAerial, 'aéreos', 'resource'),
  ].filter((d): d is DeltaChip => d !== null)
}

// ── Page ─────────────────────────────────────────────────────────────────────

function Situacao() {
  const query = useQuery(situationReportsQuery(14))
  const reports = query.data ?? []
  const latest = reports[0]
  const previous = reports[1]
  const earlier = reports.slice(1)

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-4xl px-4 py-6">
        <h1 className="mb-6 text-2xl font-bold text-foreground">
          Situação atual
        </h1>

        {query.isLoading ? (
          <HeroSkeleton />
        ) : latest ? (
          <>
            <Hero report={latest} previous={previous} />
            <TopIncidents ids={latest.topIncidentIds} />
            {earlier.length > 0 && <EarlierReports reports={earlier} />}
          </>
        ) : (
          <div className="flex flex-col items-center gap-2 py-16 text-center">
            <p className="text-sm text-muted-foreground">
              Ainda não há relatórios de situação.
            </p>
          </div>
        )}
      </main>
    </div>
  )
}

// ── Hero — the latest report ─────────────────────────────────────────────────

function Hero({
  report,
  previous,
}: {
  report: SituationReport
  previous: SituationReport | undefined
}) {
  const at = new Date(report.at)
  const deltas = previous ? deltasFor(report, previous) : []

  return (
    <section className={`${CARD_CLASS} mb-6`}>
      <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h2 className="text-sm font-semibold text-foreground">
          Ponto de situação · {slotLabel(report.slot)}
        </h2>
        <p className="text-sm tabular-nums text-muted-foreground">
          Atualizado às {timeFmt.format(at)}
        </p>
      </div>

      {/* Headline numbers */}
      <dl className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        {STAT_TILES.map(({ key, label, Icon }) => (
          <div
            key={key}
            className="rounded-xl bg-black/[0.03] p-3 dark:bg-white/[0.04]"
          >
            <dt className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
              <Icon className="size-3.5" aria-hidden />
              {label}
            </dt>
            <dd className="mt-1 text-2xl font-bold tabular-nums text-foreground">
              {numberFmt.format(report[key])}
            </dd>
          </div>
        ))}
      </dl>

      {/* Deltas vs previous */}
      {deltas.length > 0 && (
        <div className="mt-4 flex flex-wrap items-center gap-1.5">
          <span className="text-xs text-muted-foreground">
            Desde o relatório anterior:
          </span>
          {deltas.map((d) => (
            <span
              key={d.label}
              className={`rounded-full px-2.5 py-1 text-xs font-medium tabular-nums ring-1 ${DELTA_TONE[d.tone]}`}
            >
              {d.label}
            </span>
          ))}
        </div>
      )}

      {/* Narrative */}
      <p className="mt-4 whitespace-pre-line text-sm leading-relaxed text-foreground/90">
        {report.body}
      </p>
    </section>
  )
}

// ── Top incidents ────────────────────────────────────────────────────────────

function TopIncidents({ ids }: { ids: string[] }) {
  // Cross-reference the report's top-fire ids against the live incidents feed so
  // each links to the map (?incident=ID) with a name and status. Ids that have
  // windowed out of the feed render nothing.
  const query = useQuery(recentIncidentsQuery())
  const byId = new Map<string, IncidentListItem>()
  for (const inc of query.data ?? []) byId.set(inc.id, inc)

  const matched = ids
    .map((id) => byId.get(id))
    .filter((inc): inc is IncidentListItem => inc !== undefined)

  if (matched.length === 0) return null

  return (
    <section className="mb-6">
      <h2 className="mb-3 text-sm font-semibold text-foreground">
        Maiores ocorrências
      </h2>
      <ul className="grid gap-2 sm:grid-cols-2">
        {matched.map((inc) => {
          const place =
            locationParts(inc.freguesia, inc.concelho, inc.district) ||
            inc.location
          return (
            <li key={inc.id}>
              <Link
                to="/"
                search={{ incident: inc.id }}
                viewTransition
                className={`${CARD_CLASS} flex items-center gap-3 !p-3 transition-colors hover:bg-white/90 dark:hover:bg-zinc-900/80`}
              >
                <span
                  aria-hidden
                  className="size-2.5 shrink-0 rounded-full"
                  style={{ backgroundColor: statusColorForCode(inc.status.code) }}
                />
                <span className="min-w-0">
                  <span className="block truncate font-medium text-foreground">
                    {incidentTitle(inc)}
                  </span>
                  <span className="block truncate text-xs text-muted-foreground">
                    {place}
                  </span>
                </span>
                <span className="ml-auto shrink-0 text-xs text-muted-foreground">
                  {inc.status.label}
                </span>
              </Link>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

// ── Earlier reports ──────────────────────────────────────────────────────────

function EarlierReports({ reports }: { reports: SituationReport[] }) {
  return (
    <section>
      <h2 className="mb-3 text-sm font-semibold text-foreground">
        Relatórios anteriores
      </h2>
      <ul className="space-y-2">
        {reports.map((report) => (
          <li key={report.id}>
            <details className={`${CARD_CLASS} group`}>
              <summary className="flex cursor-pointer list-none items-center gap-3">
                <span className="min-w-0 flex-1">
                  <span className="block text-sm font-medium text-foreground">
                    {slotLabel(report.slot)} · {dayMonthFmt.format(new Date(report.at))}
                  </span>
                  <span className="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs tabular-nums text-muted-foreground">
                    <span>{numberFmt.format(report.activeFires)} fogos</span>
                    <span>{numberFmt.format(report.totalMan)} operacionais</span>
                    <span>{numberFmt.format(report.totalTerrain)} terrestres</span>
                    <span>{numberFmt.format(report.totalAerial)} aéreos</span>
                  </span>
                </span>
                <ChevronDown
                  aria-hidden
                  className="size-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-180"
                />
              </summary>
              <p className="mt-3 whitespace-pre-line border-t border-black/5 pt-3 text-sm leading-relaxed text-foreground/90 dark:border-white/10">
                {report.body}
              </p>
            </details>
          </li>
        ))}
      </ul>
    </section>
  )
}

// ── Skeletons ────────────────────────────────────────────────────────────────

function HeroSkeleton() {
  return (
    <div className="space-y-6">
      <div className={CARD_CLASS}>
        <Skeleton className="h-5 w-48" />
        <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-20 w-full" />
          ))}
        </div>
        <Skeleton className="mt-4 h-24 w-full" />
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-14 w-full" />
        ))}
      </div>
    </div>
  )
}
