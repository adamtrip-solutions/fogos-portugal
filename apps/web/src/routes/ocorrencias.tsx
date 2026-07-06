import { createFileRoute, Link } from '@tanstack/react-router'
import { useInfiniteQuery } from '@tanstack/react-query'
import {
  ChevronDown,
  Flame,
  Loader2,
  Plane,
  ThermometerSun,
  TrendingUp,
  TriangleAlert,
  Truck,
  Users,
} from 'lucide-react'

import {
  buildIncidentsFilter,
  incidentsPageQuery,
} from '#/lib/fogos/api.ts'
import type { IncidentsWindow } from '#/lib/fogos/api.ts'
import {
  STATUS_BUCKETS,
  STATUS_BUCKET_COLOR,
  STATUS_BUCKET_LABEL,
  formatRelative,
  incidentTitle,
  locationParts,
  statusColorForCode,
} from '#/lib/fogos/format.ts'
import type { StatusBucket } from '#/lib/fogos/format.ts'
import type { IncidentListItem } from '#/lib/fogos/types.ts'
import { IncidentRow } from '#/components/incident-row.tsx'
import { PageHeader } from '#/components/page-header.tsx'
import { Skeleton } from '#/components/ui/skeleton.tsx'

// ── Search params ────────────────────────────────────────────────────────────

interface OcorrenciasSearch {
  window: IncidentsWindow
  /** Comma-joined bucket keys; omitted when all five are selected (default). */
  status?: string
  district?: string
}

const WINDOWS: readonly IncidentsWindow[] = ['1d', '3d', '7d', '30d', 'all']
const DEFAULT_WINDOW: IncidentsWindow = '3d'

/** Parse a comma string into buckets, canonicalised to STATUS_BUCKETS order. */
function parseBuckets(raw: unknown): StatusBucket[] {
  if (typeof raw !== 'string') return []
  const chosen = new Set(raw.split(',').map((s) => s.trim()))
  return STATUS_BUCKETS.filter((b) => chosen.has(b))
}

/** Canonical `status` value: undefined when empty or all five (= default). */
function normalizeStatus(raw: unknown): string | undefined {
  const picked = parseBuckets(raw)
  if (picked.length === 0 || picked.length === STATUS_BUCKETS.length) {
    return undefined
  }
  return picked.join(',')
}

/** The selected buckets for a `status` value (undefined = all). */
function bucketsFor(status: string | undefined): StatusBucket[] {
  return status == null ? [...STATUS_BUCKETS] : parseBuckets(status)
}

function isNonDefault(search: OcorrenciasSearch): boolean {
  return (
    search.window !== DEFAULT_WINDOW ||
    search.status != null ||
    search.district != null
  )
}

// The 18 mainland + 11 island districts from backend/dev/seed/locations.json
// (level-1 rows), sorted pt-PT. Hardcoded on purpose — the list is stable.
const DISTRICTS = [
  'Aveiro',
  'Beja',
  'Braga',
  'Bragança',
  'Castelo Branco',
  'Coimbra',
  'Évora',
  'Faro',
  'Guarda',
  'Ilha da Madeira',
  'Ilha das Flores',
  'Ilha de Porto Santo',
  'Ilha de Santa Maria',
  'Ilha de São Jorge',
  'Ilha de São Miguel',
  'Ilha do Corvo',
  'Ilha do Faial',
  'Ilha do Pico',
  'Ilha Graciosa',
  'Ilha Terceira',
  'Leiria',
  'Lisboa',
  'Portalegre',
  'Porto',
  'Santarém',
  'Setúbal',
  'Viana do Castelo',
  'Vila Real',
  'Viseu',
] as const

const WINDOW_PILLS: Array<{ label: string; value: IncidentsWindow }> = [
  { label: 'Hoje', value: '1d' },
  { label: '3 dias', value: '3d' },
  { label: '7 dias', value: '7d' },
  { label: '30 dias', value: '30d' },
  { label: 'Tudo', value: 'all' },
]

function filterFor(search: OcorrenciasSearch) {
  return buildIncidentsFilter({
    window: search.window,
    buckets: bucketsFor(search.status),
    district: search.district,
  })
}

export const Route = createFileRoute('/ocorrencias')({
  component: Ocorrencias,
  validateSearch: (search: Record<string, unknown>): OcorrenciasSearch => {
    const window = WINDOWS.includes(search.window as IncidentsWindow)
      ? (search.window as IncidentsWindow)
      : DEFAULT_WINDOW
    const status = normalizeStatus(search.status)
    const district =
      typeof search.district === 'string' && search.district.trim().length > 0
        ? search.district
        : undefined
    return {
      window,
      ...(status ? { status } : {}),
      ...(district ? { district } : {}),
    }
  },
  loaderDeps: ({ search }) => search,
  loader: ({ context, deps }) =>
    context.queryClient
      .ensureInfiniteQueryData(incidentsPageQuery(filterFor(deps)))
      .catch(() => null),
})

// ── Styling tokens (shared with the rest of the app) ─────────────────────────

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60'

const PILL_SELECTED =
  'rounded-full bg-orange-500/15 px-2.5 py-1 text-xs font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
const PILL_IDLE =
  'rounded-full bg-muted/60 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted'

// ── Lisbon date formatting for the "Início" column ───────────────────────────

const startTimeFmt = new Intl.DateTimeFormat('pt-PT', {
  timeZone: 'Europe/Lisbon',
  hour: '2-digit',
  minute: '2-digit',
})
const startDayMonthFmt = new Intl.DateTimeFormat('pt-PT', {
  timeZone: 'Europe/Lisbon',
  day: 'numeric',
  month: 'short',
})
const lisbonDayKeyFmt = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Lisbon',
})

/** `HH:mm` when the incident started today (Lisbon), else `d MMM, HH:mm`. */
function formatStart(iso: string): string {
  const d = new Date(iso)
  const today = lisbonDayKeyFmt.format(new Date()) === lisbonDayKeyFmt.format(d)
  return today
    ? startTimeFmt.format(d)
    : `${startDayMonthFmt.format(d)}, ${startTimeFmt.format(d)}`
}

// ── Page ─────────────────────────────────────────────────────────────────────

function Ocorrencias() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()

  const query = useInfiniteQuery(incidentsPageQuery(filterFor(search)))
  const pages = query.data?.pages ?? []
  const incidents = pages.flatMap((p) => p.incidents.nodes)
  const totalCount = pages[0]?.incidents.totalCount
  const nonDefault = isNonDefault(search)

  const setWindow = (value: IncidentsWindow) =>
    navigate({ search: (p) => ({ ...p, window: value }), replace: true })

  const toggleBucket = (bucket: StatusBucket) => {
    const set = new Set(bucketsFor(search.status))
    if (set.has(bucket)) set.delete(bucket)
    else set.add(bucket)
    const next = normalizeStatus([...set].join(','))
    navigate({ search: (p) => ({ ...p, status: next }), replace: true })
  }

  const setDistrict = (value: string) =>
    navigate({
      search: (p) => ({ ...p, district: value || undefined }),
      replace: true,
    })

  const clear = () => navigate({ search: { window: DEFAULT_WINDOW }, replace: true })

  const selectedBuckets = new Set(bucketsFor(search.status))

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-6xl px-4 py-6">
        {/* Header */}
        <div className="mb-6 flex items-end justify-between gap-4">
          <h1 className="text-2xl font-bold text-foreground">Ocorrências</h1>
          {query.isLoading ? (
            <Skeleton className="h-5 w-24" />
          ) : totalCount != null ? (
            <span className="text-sm tabular-nums text-muted-foreground">
              {totalCount} ocorrências
            </span>
          ) : null}
        </div>

        {/* Filter bar */}
        <div className={`${CARD_CLASS} mb-6`}>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
            {/* Window */}
            <div className="flex flex-wrap gap-1.5">
              {WINDOW_PILLS.map((pill) => (
                <button
                  key={pill.value}
                  type="button"
                  aria-pressed={search.window === pill.value}
                  onClick={() => setWindow(pill.value)}
                  className={
                    search.window === pill.value ? PILL_SELECTED : PILL_IDLE
                  }
                >
                  {pill.label}
                </button>
              ))}
            </div>

            {/* Buckets */}
            <div className="flex flex-wrap gap-1.5">
              {STATUS_BUCKETS.map((bucket) => {
                const selected = selectedBuckets.has(bucket)
                const color = STATUS_BUCKET_COLOR[bucket]
                return (
                  <button
                    key={bucket}
                    type="button"
                    aria-pressed={selected}
                    onClick={() => toggleBucket(bucket)}
                    className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium transition-colors ${
                      selected
                        ? 'text-foreground'
                        : 'bg-muted/60 text-muted-foreground opacity-60 hover:opacity-100'
                    }`}
                    style={
                      selected
                        ? {
                            backgroundColor: `${color}1f`,
                            boxShadow: `inset 0 0 0 1px ${color}66`,
                          }
                        : undefined
                    }
                  >
                    <span
                      aria-hidden
                      className="size-2 rounded-full"
                      style={{ backgroundColor: color }}
                    />
                    {STATUS_BUCKET_LABEL[bucket]}
                  </button>
                )
              })}
            </div>

            {/* District */}
            <div className="relative">
              <select
                aria-label="Distrito"
                value={search.district ?? ''}
                onChange={(e) => setDistrict(e.target.value)}
                className="appearance-none rounded-xl border border-black/10 bg-white/70 py-1.5 pl-3 pr-8 text-sm font-medium text-foreground transition-colors hover:bg-white/90 focus:outline-none focus:ring-2 focus:ring-orange-500/40 dark:border-white/15 dark:bg-zinc-900/60 dark:hover:bg-zinc-900/80"
              >
                <option value="">Todos os distritos</option>
                {DISTRICTS.map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
              <ChevronDown
                aria-hidden
                className="pointer-events-none absolute right-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
              />
            </div>

            {nonDefault && (
              <button
                type="button"
                onClick={clear}
                className="text-xs font-medium text-orange-600 transition-colors hover:text-orange-700 dark:text-orange-400 dark:hover:text-orange-300"
              >
                Limpar
              </button>
            )}
          </div>
        </div>

        {/* Results */}
        <Results
          incidents={incidents}
          isLoading={query.isLoading}
          isError={query.isError}
          hasNextPage={query.hasNextPage}
          isFetchingNextPage={query.isFetchingNextPage}
          onLoadMore={() => query.fetchNextPage()}
          totalCount={totalCount}
          onClear={clear}
        />
      </main>
    </div>
  )
}

// ── Results (table on md+, card list below) ──────────────────────────────────

const TABLE_COLUMNS = ['Estado', 'Local', 'Início', 'Meios', 'Atualização', '']

function Results({
  incidents,
  isLoading,
  isError,
  hasNextPage,
  isFetchingNextPage,
  onLoadMore,
  totalCount,
  onClear,
}: {
  incidents: IncidentListItem[]
  isLoading: boolean
  isError: boolean
  hasNextPage: boolean
  isFetchingNextPage: boolean
  onLoadMore: () => void
  totalCount: number | undefined
  onClear: () => void
}) {
  if (isLoading) return <TableSkeleton />

  if (isError && incidents.length === 0) {
    return (
      <div className={`${CARD_CLASS} py-10 text-center`}>
        <p className="text-sm text-muted-foreground">
          Não foi possível carregar as ocorrências. A tentar novamente…
        </p>
      </div>
    )
  }

  if (incidents.length === 0) {
    return (
      <div className="flex flex-col items-center gap-3 py-16 text-center">
        <p className="text-sm text-muted-foreground">
          Sem ocorrências para os filtros selecionados.
        </p>
        <button
          type="button"
          onClick={onClear}
          className="text-sm font-medium text-orange-600 transition-colors hover:text-orange-700 dark:text-orange-400 dark:hover:text-orange-300"
        >
          Limpar filtros
        </button>
      </div>
    )
  }

  return (
    <>
      {/* Desktop table */}
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border/60 text-left text-xs uppercase tracking-wider text-muted-foreground">
              {TABLE_COLUMNS.map((col, i) => (
                <th key={i} className="py-2 pr-4 font-medium">
                  {col}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {incidents.map((inc) => (
              <IncidentTableRow key={inc.id} incident={inc} />
            ))}
          </tbody>
        </table>
      </div>

      {/* Mobile card list */}
      <ul className="space-y-2 md:hidden">
        {incidents.map((inc) => (
          <IncidentRow key={inc.id} incident={inc} />
        ))}
      </ul>

      {/* Pagination */}
      {hasNextPage && (
        <div className="mt-6 flex flex-col items-center gap-2">
          <button
            type="button"
            onClick={onLoadMore}
            disabled={isFetchingNextPage}
            className="inline-flex items-center gap-2 rounded-xl border border-black/5 bg-white/75 px-4 py-2 text-sm font-medium text-foreground shadow-sm backdrop-blur-xl transition-colors hover:bg-white/90 disabled:opacity-60 dark:border-white/10 dark:bg-zinc-900/70 dark:hover:bg-zinc-900/90"
          >
            {isFetchingNextPage && <Loader2 className="size-4 animate-spin" />}
            Carregar mais
          </button>
          {totalCount != null && (
            <p className="text-xs tabular-nums text-muted-foreground">
              A mostrar {incidents.length} de {totalCount}
            </p>
          )}
        </div>
      )}
    </>
  )
}

function IncidentTableRow({ incident }: { incident: IncidentListItem }) {
  const place =
    locationParts(null, incident.concelho, incident.district) ||
    incident.location

  return (
    <tr className="border-b border-border/40 transition-colors last:border-0 hover:bg-black/[0.03] dark:hover:bg-white/[0.04]">
      {/* Estado */}
      <td className="py-3 pr-4 align-top">
        <span className="inline-flex items-center gap-2">
          <span
            aria-hidden
            className="size-2.5 shrink-0 rounded-full"
            style={{ backgroundColor: statusColorForCode(incident.status.code) }}
          />
          <span className="text-foreground">{incident.status.label}</span>
        </span>
      </td>

      {/* Local */}
      <td className="py-3 pr-4 align-top">
        <Link
          to="/"
          search={{ incident: incident.id }}
          className="font-medium text-foreground underline-offset-2 hover:underline"
        >
          {incidentTitle(incident)}
        </Link>
        <p className="truncate text-xs text-muted-foreground">{place}</p>
      </td>

      {/* Início */}
      <td className="py-3 pr-4 align-top tabular-nums text-muted-foreground">
        {formatStart(incident.occurredAt)}
      </td>

      {/* Meios */}
      <td className="py-3 pr-4 align-top">
        <ResourceCell resources={incident.resources} />
      </td>

      {/* Atualização */}
      <td className="py-3 pr-4 align-top text-muted-foreground">
        {formatRelative(incident.updatedAt)}
      </td>

      {/* Flags */}
      <td className="py-3 align-top">
        <IncidentFlags incident={incident} />
      </td>
    </tr>
  )
}

const RESOURCE_ICONS = [
  { key: 'man', Icon: Users },
  { key: 'terrain', Icon: Truck },
  { key: 'aerial', Icon: Plane },
] as const

function ResourceCell({
  resources,
}: {
  resources: IncidentListItem['resources']
}) {
  return (
    <div className="flex items-center gap-3 text-muted-foreground">
      {RESOURCE_ICONS.map(({ key, Icon }) => (
        <span key={key} className="inline-flex items-center gap-1">
          <Icon className="size-3.5" aria-hidden />
          <span className="tabular-nums">
            {resources[key] > 0 ? resources[key] : '—'}
          </span>
        </span>
      ))}
    </div>
  )
}

function IncidentFlags({ incident }: { incident: IncidentListItem }) {
  const flags: Array<{ Icon: typeof Flame; title: string; className: string }> =
    []
  if (incident.important) {
    flags.push({
      Icon: TriangleAlert,
      title: 'Ocorrência importante',
      className: 'text-amber-600 dark:text-amber-400',
    })
  }
  if (incident.signals.escalating) {
    flags.push({
      Icon: TrendingUp,
      title: 'Em escalada',
      className: 'text-amber-600 dark:text-amber-400',
    })
  }
  if (incident.signals.rekindle) {
    flags.push({
      Icon: Flame,
      title: 'Reacendimento',
      className: 'text-red-600 dark:text-red-400',
    })
  }
  if (incident.signals.criticalConditions) {
    flags.push({
      Icon: ThermometerSun,
      title: 'Condições críticas',
      className: 'text-red-700 dark:text-red-300',
    })
  }
  if (flags.length === 0) return null

  return (
    <div className="flex items-center gap-2">
      {flags.map(({ Icon, title, className }) => (
        <span key={title} title={title} className={className}>
          <Icon className="size-4" aria-hidden />
          <span className="sr-only">{title}</span>
        </span>
      ))}
    </div>
  )
}

function TableSkeleton() {
  return (
    <div className="space-y-2">
      {Array.from({ length: 8 }).map((_, i) => (
        <Skeleton key={i} className="h-14 w-full" />
      ))}
    </div>
  )
}
