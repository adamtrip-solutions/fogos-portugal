import { useMemo, useRef, useState } from 'react'
import { ClientOnly, Link, createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Layer, Map, Source } from 'react-map-gl/maplibre'
import type {
  FillLayerSpecification,
  LineLayerSpecification,
  MapLayerMouseEvent,
} from 'react-map-gl/maplibre'
import { ArrowRight, Loader2, Search, X } from 'lucide-react'

import {
  concelhoProfileQuery,
  fireRiskCountryQuery,
} from '#/lib/fogos/api.ts'
import type { RiskDay, RiskFeatureCollection } from '#/lib/fogos/api.ts'
import { concelhoByDico, searchConcelhos } from '#/lib/fogos/concelhos.ts'
import type { ConcelhoEntry } from '#/lib/fogos/concelhos.ts'
import { useTheme } from '#/lib/theme.ts'
import type { Theme } from '#/lib/theme.ts'
import { PageHeader } from '#/components/page-header.tsx'
import {
  RISK_LEVELS,
  RISK_STYLE,
  RISK_UNKNOWN,
  RiskStrip,
} from '#/components/risk-strip.tsx'

// ── Search params ────────────────────────────────────────────────────────────

interface RiscoSearch {
  dia: RiskDay
  /** DICO of the selected concelho (detail card + map highlight). */
  concelho?: string
}

const DAYS: readonly RiskDay[] = ['TODAY', 'TOMORROW', 'AFTER']
const DEFAULT_DAY: RiskDay = 'TODAY'

const DAY_PILLS: Array<{ label: string; value: RiskDay }> = [
  { label: 'Hoje', value: 'TODAY' },
  { label: 'Amanhã', value: 'TOMORROW' },
  { label: 'Depois de amanhã', value: 'AFTER' },
]

export const Route = createFileRoute('/risco')({
  head: () => ({
    meta: [{ title: 'Risco de incêndio — FogosPortugal' }],
  }),
  validateSearch: (search: Record<string, unknown>): RiscoSearch => {
    const dia = DAYS.includes(search.dia as RiskDay)
      ? (search.dia as RiskDay)
      : DEFAULT_DAY
    const concelho =
      typeof search.concelho === 'string' && search.concelho.trim().length > 0
        ? search.concelho
        : undefined
    return { dia, ...(concelho ? { concelho } : {}) }
  },
  loaderDeps: ({ search }) => ({ dia: search.dia }),
  loader: ({ context, deps }) =>
    context.queryClient
      .ensureQueryData(fireRiskCountryQuery(deps.dia))
      .catch(() => null),
  component: Risco,
})

// ── Styling tokens (shared with the rest of the app) ─────────────────────────

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60'
const PILL_SELECTED =
  'rounded-full bg-orange-500/15 px-3 py-1.5 text-sm font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
const PILL_IDLE =
  'rounded-full bg-muted/60 px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted'

const forecastFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
})

/** "Previsão de 7 jul." for a stored YYYY-MM-DD forecast date. */
function formatForecast(date: string | null | undefined): string | null {
  if (!date) return null
  return `Previsão de ${forecastFmt.format(new Date(`${date}T00:00:00`))}`
}

// ── Page ─────────────────────────────────────────────────────────────────────

function Risco() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const theme = useTheme()

  const risk = useQuery(fireRiskCountryQuery(search.dia))
  const geoJson = risk.data?.geoJson ?? null
  const forecast = formatForecast(risk.data?.forecastDate)
  const selectedDico = search.concelho ?? null

  const setDay = (value: RiskDay) =>
    navigate({ search: (p) => ({ ...p, dia: value }), replace: true })

  const selectConcelho = (dico: string | null) =>
    navigate({
      search: (p) => ({ ...p, concelho: dico ?? undefined }),
      replace: true,
    })

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-5xl px-4 py-6">
        {/* Header */}
        <div className="mb-5 flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <h1 className="text-2xl font-bold text-foreground">
              Risco de incêndio
            </h1>
            <p className="mt-0.5 text-sm text-muted-foreground">
              {forecast ?? 'Índice de risco de incêndio rural (IPMA)'}
            </p>
          </div>

          {/* Day segmented control */}
          <div className="flex flex-wrap gap-1.5">
            {DAY_PILLS.map((pill) => (
              <button
                key={pill.value}
                type="button"
                aria-pressed={search.dia === pill.value}
                onClick={() => setDay(pill.value)}
                className={search.dia === pill.value ? PILL_SELECTED : PILL_IDLE}
              >
                {pill.label}
              </button>
            ))}
          </div>
        </div>

        {/* Map + legend */}
        <div className={`${CARD_CLASS} p-3`}>
          <div className="relative h-[60vh] min-h-80 overflow-hidden rounded-xl bg-zinc-100 dark:bg-zinc-800">
            {risk.isLoading && !risk.data ? (
              <div className="flex h-full items-center justify-center">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            ) : !geoJson ? (
              <div className="flex h-full items-center justify-center px-6 text-center">
                <p className="text-sm text-muted-foreground">
                  Sem previsão disponível de momento.
                </p>
              </div>
            ) : (
              <ClientOnly
                fallback={
                  <div className="flex h-full items-center justify-center">
                    <Loader2 className="size-6 animate-spin text-muted-foreground" />
                  </div>
                }
              >
                <RiskMap
                  data={geoJson}
                  theme={theme}
                  selectedDico={selectedDico}
                  onSelect={selectConcelho}
                />
              </ClientOnly>
            )}
          </div>

          <Legend />
        </div>

        {/* Concelho detail */}
        <section className="mt-6">
          <div className="mb-3 flex items-center justify-between gap-3">
            <h2 className="text-base font-semibold text-foreground">
              Detalhe por concelho
            </h2>
            <div className="w-full max-w-xs">
              <ConcelhoSearch value={selectedDico} onSelect={selectConcelho} />
            </div>
          </div>
          {selectedDico ? (
            <ConcelhoDetail dico={selectedDico} onClose={() => selectConcelho(null)} />
          ) : (
            <p className={`${CARD_CLASS} text-center text-sm text-muted-foreground`}>
              Selecione um concelho no mapa ou pesquise acima para ver a
              previsão a 5 dias.
            </p>
          )}
        </section>
      </main>
    </div>
  )
}

// ── Map ──────────────────────────────────────────────────────────────────────

const LIGHT_STYLE = 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json'
const DARK_STYLE = 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json'

// Level → fill colour, built once from the shared palette. Level 0 (no value
// for the horizon) falls through to the neutral swatch.
const FILL_COLOR_MATCH = [
  'match',
  ['get', 'level'],
  ...RISK_LEVELS.flatMap((l) => [l, RISK_STYLE[l].bg]),
  RISK_UNKNOWN.bg,
] as unknown as NonNullable<FillLayerSpecification['paint']>['fill-color']

const fillPaint: FillLayerSpecification['paint'] = {
  'fill-color': FILL_COLOR_MATCH,
  'fill-opacity': 0.75,
}

interface HoverInfo {
  dico: string
  name: string
  level: number
  x: number
  y: number
}

function RiskMap({
  data,
  theme,
  selectedDico,
  onSelect,
}: {
  data: RiskFeatureCollection
  theme: Theme
  selectedDico: string | null
  onSelect: (dico: string | null) => void
}) {
  const hoveredDicoRef = useRef<string | null>(null)
  const [hover, setHover] = useState<HoverInfo | null>(null)

  const borderPaint = useMemo<LineLayerSpecification['paint']>(
    () => ({
      'line-color':
        theme === 'dark' ? 'rgba(255,255,255,0.18)' : 'rgba(0,0,0,0.18)',
      'line-width': 0.5,
    }),
    [theme],
  )

  const hoverPaint: LineLayerSpecification['paint'] = {
    'line-color': theme === 'dark' ? '#ffffff' : '#18181b',
    'line-width': 1.5,
  }
  const selectedPaint: LineLayerSpecification['paint'] = {
    'line-color': '#ea580c',
    'line-width': 2.5,
  }

  const handleMouseMove = (event: MapLayerMouseEvent) => {
    const map = event.target
    const feature = event.features?.[0]
    map.getCanvas().style.cursor = feature ? 'pointer' : ''

    const props = feature?.properties as
      | { dico?: string; name?: string; level?: number }
      | undefined
    const dico = props?.dico ?? null
    if (dico === hoveredDicoRef.current) return
    hoveredDicoRef.current = dico
    setHover(
      dico
        ? {
            dico,
            name: props?.name ?? '',
            level: typeof props?.level === 'number' ? props.level : 0,
            x: event.point.x,
            y: event.point.y,
          }
        : null,
    )
  }

  const handleMouseLeave = (event: MapLayerMouseEvent) => {
    event.target.getCanvas().style.cursor = ''
    hoveredDicoRef.current = null
    setHover(null)
  }

  const handleClick = (event: MapLayerMouseEvent) => {
    const props = event.features?.[0]?.properties as { dico?: string } | undefined
    onSelect(props?.dico ?? null)
  }

  const hoverLabel = hover ? riskLabel(hover.level) : ''

  return (
    <>
      <Map
        mapStyle={theme === 'dark' ? DARK_STYLE : LIGHT_STYLE}
        initialViewState={{
          bounds: [
            [-9.6, 36.8],
            [-6.1, 42.2],
          ],
          fitBoundsOptions: { padding: 24 },
        }}
        attributionControl={{ compact: true }}
        interactiveLayerIds={['risk-fill']}
        onMouseMove={handleMouseMove}
        onMouseLeave={handleMouseLeave}
        onClick={handleClick}
        style={{ position: 'absolute', inset: 0 }}
      >
        <Source id="risk" type="geojson" data={data}>
          <Layer id="risk-fill" type="fill" paint={fillPaint} />
          <Layer id="risk-border" type="line" paint={borderPaint} />
          <Layer
            id="risk-hover"
            type="line"
            filter={['==', ['get', 'dico'], hover?.dico ?? '__none__']}
            paint={hoverPaint}
          />
          <Layer
            id="risk-selected"
            type="line"
            filter={['==', ['get', 'dico'], selectedDico ?? '__none__']}
            paint={selectedPaint}
          />
        </Source>
      </Map>

      {hover && (
        <div
          className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-[calc(100%+10px)] whitespace-nowrap rounded-lg border border-black/10 bg-white/95 px-2.5 py-1.5 text-xs shadow-lg backdrop-blur-sm dark:border-white/10 dark:bg-zinc-900/95"
          style={{ left: hover.x, top: hover.y }}
        >
          <p className="font-semibold text-foreground">{hover.name}</p>
          <p className="flex items-center gap-1.5 text-muted-foreground">
            <span
              aria-hidden
              className="size-2 rounded-full"
              style={{ backgroundColor: riskStyleOf(hover.level).bg }}
            />
            {hoverLabel}
          </p>
        </div>
      )}
    </>
  )
}

function riskStyleOf(level: number) {
  return RISK_STYLE[level] ?? RISK_UNKNOWN
}

function riskLabel(level: number): string {
  return level >= 1 && level <= 5 ? RISK_STYLE[level].label : 'Sem dados'
}

// ── Legend ───────────────────────────────────────────────────────────────────

function Legend() {
  return (
    <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2 px-1">
      {RISK_LEVELS.map((l) => (
        <span key={l} className="inline-flex items-center gap-1.5">
          <span
            aria-hidden
            className="size-3 rounded-full"
            style={{ backgroundColor: RISK_STYLE[l].bg }}
          />
          <span className="text-xs text-muted-foreground">
            {RISK_STYLE[l].label}
          </span>
        </span>
      ))}
    </div>
  )
}

// ── Concelho search combobox ─────────────────────────────────────────────────

function ConcelhoSearch({
  value,
  onSelect,
}: {
  value: string | null
  onSelect: (dico: string | null) => void
}) {
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const blurTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const selected = value ? concelhoByDico(value) : null
  const results: ConcelhoEntry[] = useMemo(
    () => (query.trim() ? searchConcelhos(query, 8) : []),
    [query],
  )

  const pick = (dico: string) => {
    onSelect(dico)
    setQuery('')
    setOpen(false)
  }

  return (
    <div className="relative">
      <div className="relative">
        <Search
          aria-hidden
          className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
        />
        <input
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => {
            blurTimer.current = setTimeout(() => setOpen(false), 120)
          }}
          placeholder={selected ? selected.name : 'Procurar concelho…'}
          className="w-full rounded-xl border border-black/10 bg-white/70 py-2 pl-8 pr-8 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-orange-500/40 dark:border-white/15 dark:bg-zinc-900/60"
        />
        {selected && (
          <button
            type="button"
            aria-label="Limpar concelho"
            onClick={() => {
              setQuery('')
              onSelect(null)
            }}
            className="absolute right-2 top-1/2 flex size-5 -translate-y-1/2 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
          >
            <X className="size-3.5" aria-hidden />
          </button>
        )}
      </div>

      {open && results.length > 0 && (
        <ul
          className="absolute left-0 right-0 top-full z-20 mt-1 max-h-72 overflow-y-auto rounded-xl border border-black/10 bg-white/95 p-1 shadow-xl backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/95"
          onMouseDown={() => {
            // Keep focus (and the list) alive through the click.
            if (blurTimer.current) clearTimeout(blurTimer.current)
          }}
        >
          {results.map((c) => (
            <li key={c.dico}>
              <button
                type="button"
                onClick={() => pick(c.dico)}
                className="flex w-full items-center justify-between gap-2 rounded-lg px-2.5 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-black/5 dark:hover:bg-white/10"
              >
                <span>{c.name}</span>
                <span className="text-xs text-muted-foreground">{c.district}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

// ── Concelho detail card ─────────────────────────────────────────────────────

function ConcelhoDetail({
  dico,
  onClose,
}: {
  dico: string
  onClose: () => void
}) {
  const { data, isLoading, isError } = useQuery(concelhoProfileQuery(dico))
  const fallback = concelhoByDico(dico)

  const name = data?.name ?? fallback?.name ?? 'Concelho'
  const district = data?.district ?? fallback?.district ?? ''
  const risk = data?.risk ?? []

  return (
    <div className={CARD_CLASS}>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          {district && (
            <p className="text-xs font-medium text-muted-foreground">{district}</p>
          )}
          <h3 className="text-lg font-bold text-foreground">{name}</h3>
        </div>
        <button
          type="button"
          aria-label="Fechar detalhe"
          onClick={onClose}
          className="flex size-7 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
        >
          <X className="size-4" aria-hidden />
        </button>
      </div>

      {isLoading && !data ? (
        <div className="flex h-24 items-center justify-center">
          <Loader2 className="size-5 animate-spin text-muted-foreground" />
        </div>
      ) : risk.length > 0 ? (
        <RiskStrip risk={risk} />
      ) : (
        <p className="py-4 text-center text-sm text-muted-foreground">
          {isError
            ? 'Não foi possível carregar a previsão. A tentar novamente…'
            : 'Sem previsão de risco para este concelho.'}
        </p>
      )}

      <div className="mt-4">
        <Link
          to="/concelho/$dico"
          params={{ dico }}
          viewTransition
          className="inline-flex items-center gap-1 text-sm font-medium text-orange-600 transition-colors hover:text-orange-700 dark:text-orange-400 dark:hover:text-orange-300"
        >
          Ver perfil do concelho
          <ArrowRight className="size-4" aria-hidden />
        </Link>
      </div>
    </div>
  )
}
