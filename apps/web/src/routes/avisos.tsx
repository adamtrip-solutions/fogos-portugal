import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Clock, ExternalLink, TriangleAlert } from 'lucide-react'

import { weatherWarningsQuery } from '#/lib/fogos/api.ts'
import type { WeatherWarning } from '#/lib/fogos/types.ts'
import { pageMeta } from '#/lib/seo.ts'
import { PageHeader } from '#/components/page-header.tsx'
import { Skeleton } from '#/components/ui/skeleton.tsx'

export const Route = createFileRoute('/avisos')({
  head: () =>
    pageMeta({
      title: 'Avisos — FogosPortugal',
      description:
        'Avisos meteorológicos oficiais do IPMA em vigor para Portugal continental, agrupados por distrito e por nível de gravidade.',
      path: '/avisos',
    }),
  component: Avisos,
  loader: ({ context }) =>
    context.queryClient.ensureQueryData(weatherWarningsQuery()).catch(() => null),
})

// ── Styling tokens (shared with the rest of the app) ─────────────────────────

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60'

// ── Level presentation ───────────────────────────────────────────────────────
//
// Colors match the concelho profile palette; label comes from the API's `levelPt`.

const LEVEL_COLOR: Record<string, string> = {
  yellow: '#F5B301',
  orange: '#FF6E02',
  red: '#B81E1F',
}

const LEVEL_RANK: Record<string, number> = { red: 3, orange: 2, yellow: 1 }

function levelColor(level: string): string {
  return LEVEL_COLOR[level.toLowerCase()] ?? '#BDBDBD'
}

function levelRank(level: string): number {
  return LEVEL_RANK[level.toLowerCase()] ?? 0
}

// ── IPMA area code → district name (mirrors the API's IpmaAreaCatalog) ────────

const AREA_TO_DISTRICT: Record<string, string> = {
  AVR: 'Aveiro',
  BJA: 'Beja',
  BGC: 'Bragança',
  BRG: 'Braga',
  CBR: 'Coimbra',
  CTB: 'Castelo Branco',
  EVR: 'Évora',
  FAR: 'Faro',
  GDA: 'Guarda',
  LRA: 'Leiria',
  LSB: 'Lisboa',
  PTG: 'Portalegre',
  PTO: 'Porto',
  STR: 'Santarém',
  STB: 'Setúbal',
  VCT: 'Viana do Castelo',
  VRL: 'Vila Real',
  VIS: 'Viseu',
  MCN: 'Madeira',
  MCS: 'Madeira',
  MMT: 'Madeira',
  PSA: 'Madeira',
  AOC: 'Açores',
  ACE: 'Açores',
  AOR: 'Açores',
}

function districtFor(areaCode: string): string {
  return AREA_TO_DISTRICT[areaCode.trim().toUpperCase()] ?? areaCode
}

// ── Validity formatting (pt-PT weekday + time) ───────────────────────────────

const VALIDITY_FMT = new Intl.DateTimeFormat('pt-PT', {
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

/** "até sáb, 18:00" — the end matters most for warnings already in force. */
function validityLabel(warning: WeatherWarning): string {
  return `até ${VALIDITY_FMT.format(new Date(warning.endsAt))}`
}

// ── Grouping ─────────────────────────────────────────────────────────────────

interface DistrictGroup {
  district: string
  maxRank: number
  warnings: WeatherWarning[]
}

function groupByDistrict(warnings: WeatherWarning[]): DistrictGroup[] {
  const byDistrict = new Map<string, WeatherWarning[]>()
  for (const w of warnings) {
    const district = districtFor(w.areaCode)
    const list = byDistrict.get(district)
    if (list) list.push(w)
    else byDistrict.set(district, [w])
  }

  return [...byDistrict.entries()]
    .map(([district, list]) => ({
      district,
      maxRank: Math.max(...list.map((w) => levelRank(w.level))),
      warnings: [...list].sort((a, b) => levelRank(b.level) - levelRank(a.level)),
    }))
    // Highest severity first, then alphabetically by district.
    .sort(
      (a, b) =>
        b.maxRank - a.maxRank || a.district.localeCompare(b.district, 'pt'),
    )
}

// ── Page ─────────────────────────────────────────────────────────────────────

function Avisos() {
  const query = useQuery(weatherWarningsQuery())
  const groups = groupByDistrict(query.data ?? [])

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-3xl px-4 py-6">
        <header className="mb-6">
          <h1 className="text-2xl font-bold text-foreground">
            Avisos meteorológicos
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Avisos oficiais do IPMA em vigor para Portugal continental.
          </p>
          <a
            href="https://www.ipma.pt"
            target="_blank"
            rel="noopener noreferrer"
            className="mt-2 inline-flex items-center gap-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
          >
            Fonte: IPMA
            <ExternalLink className="size-3" aria-hidden />
          </a>
        </header>

        {query.isLoading ? (
          <ListSkeleton />
        ) : groups.length === 0 ? (
          <div className="flex flex-col items-center gap-2 py-16 text-center">
            <p className="text-sm text-muted-foreground">
              Sem avisos meteorológicos em vigor.
            </p>
          </div>
        ) : (
          <div className="space-y-3">
            {groups.map((group) => (
              <DistrictCard key={group.district} group={group} />
            ))}
          </div>
        )}
      </main>
    </div>
  )
}

function DistrictCard({ group }: { group: DistrictGroup }) {
  return (
    <section className={CARD_CLASS}>
      <h2 className="mb-3 text-base font-semibold text-foreground">
        {group.district}
      </h2>
      <ul className="space-y-3">
        {group.warnings.map((w) => (
          <WarningRow key={w.id} warning={w} />
        ))}
      </ul>
    </section>
  )
}

function WarningRow({ warning }: { warning: WeatherWarning }) {
  const color = levelColor(warning.level)
  return (
    <li className="flex gap-3">
      <span
        className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-lg"
        style={{ backgroundColor: `${color}22` }}
      >
        <TriangleAlert className="size-4" style={{ color }} aria-hidden />
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span
            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
            style={{ backgroundColor: `${color}22`, color }}
          >
            {warning.levelPt}
          </span>
          <span className="text-sm font-medium text-foreground">
            {warning.awarenessType}
          </span>
          <span className="ml-auto flex items-center gap-1 text-xs text-muted-foreground">
            <Clock className="size-3" aria-hidden />
            {validityLabel(warning)}
          </span>
        </div>
        {warning.text && (
          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
            {warning.text}
          </p>
        )}
      </div>
    </li>
  )
}

function ListSkeleton() {
  return (
    <div className="space-y-3">
      {Array.from({ length: 4 }).map((_, i) => (
        <Skeleton key={i} className="h-28 w-full" />
      ))}
    </div>
  )
}
