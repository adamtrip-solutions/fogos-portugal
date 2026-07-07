import { useMemo, useState } from 'react'
import { ClientOnly, createFileRoute, Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Helicopter, Loader2, Plane } from 'lucide-react'

import {
  activeIncidentsQuery,
  aircraftFleetQuery,
  aircraftTrackQuery,
} from '#/lib/fogos/api.ts'
import { formatRelative, locationParts } from '#/lib/fogos/format.ts'
import type { FleetAircraft, IncidentListItem } from '#/lib/fogos/types.ts'
import { useTheme } from '#/lib/theme.ts'
import { AircraftMap } from '#/components/aircraft-map.tsx'
import { pageMeta } from '#/lib/seo.ts'
import { PageHeader } from '#/components/page-header.tsx'

export const Route = createFileRoute('/aeronaves')({
  head: () => ({
    ...pageMeta({
      title: 'Aeronaves — FogosPortugal',
      description:
        'Aeronaves de combate a incêndios em Portugal em direto: aviões e helicópteros no ar, últimas posições, trajetos e o incêndio a que estão alocados.',
      path: '/aeronaves',
    }),
  }),
  component: Aeronaves,
  // SSR the first fleet fetch (same server-fn pattern as the other pages);
  // client-side polling then keeps it live. Swallow prefetch failures so the
  // page still renders and the client query surfaces the error.
  loader: ({ context }) =>
    context.queryClient.ensureQueryData(aircraftFleetQuery()).catch(() => null),
})

// ── Helpers ──────────────────────────────────────────────────────────────────

function isHelicopter(kind: string | null): boolean {
  const k = kind?.toLowerCase() ?? ''
  return k.includes('heli') || k.includes('rotor')
}

/** The most human label for an aircraft — its name, else tail, else ICAO. */
function aircraftLabel(a: FleetAircraft): string {
  return (
    a.tracked.name?.trim() ||
    a.tracked.registration?.trim() ||
    a.tracked.icao.toUpperCase()
  )
}

/** Secondary descriptor: registration (when not already the label) + model. */
function aircraftSubtitle(a: FleetAircraft): string {
  const label = aircraftLabel(a)
  const parts: string[] = []
  const reg = a.tracked.registration?.trim()
  if (reg && reg !== label) parts.push(reg)
  const model = a.tracked.type?.trim() || kindLabel(a.tracked.kind)
  if (model) parts.push(model)
  return parts.join(' · ')
}

function kindLabel(kind: string | null): string {
  const k = kind?.toLowerCase() ?? ''
  if (k.includes('heli') || k.includes('rotor')) return 'Helicóptero'
  if (k.includes('plane') || k.includes('avi')) return 'Avião'
  return kind?.trim() ?? ''
}

/** Sort: active first, then most-recent position; aircraft without one last. */
function sortFleet(fleet: FleetAircraft[]): FleetAircraft[] {
  return [...fleet].sort((a, b) => {
    if (a.active !== b.active) return a.active ? -1 : 1
    const ta = a.position ? Date.parse(a.position.sampledAt) : -Infinity
    const tb = b.position ? Date.parse(b.position.sampledAt) : -Infinity
    return tb - ta
  })
}

// ── Page ─────────────────────────────────────────────────────────────────────

function Aeronaves() {
  const theme = useTheme()
  const [selectedIcao, setSelectedIcao] = useState<string | null>(null)

  const fleetQuery = useQuery(aircraftFleetQuery())
  const incidentsQuery = useQuery(activeIncidentsQuery())
  const trackQuery = useQuery({
    ...aircraftTrackQuery(selectedIcao ?? ''),
    enabled: selectedIcao != null,
  })

  const fleet = fleetQuery.data ?? []
  const sorted = useMemo(() => sortFleet(fleet), [fleet])
  const activeCount = fleet.filter((a) => a.active).length

  // Resolve currentIncidentId → a human place label via the active-incidents feed.
  const incidentsById = useMemo(() => {
    const map = new Map<string, IncidentListItem>()
    for (const inc of incidentsQuery.data ?? []) map.set(inc.id, inc)
    return map
  }, [incidentsQuery.data])

  const select = (icao: string) =>
    setSelectedIcao((prev) => (prev === icao ? null : icao))

  return (
    <div className="flex h-[100dvh] flex-col bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />

      {/* Title + live summary */}
      <div className="mx-auto flex w-full max-w-6xl items-end justify-between gap-4 px-4 pt-6 pb-4">
        <h1 className="text-2xl font-bold text-foreground">Aeronaves</h1>
        {fleetQuery.isLoading ? (
          <span className="h-5 w-40 animate-pulse rounded bg-muted" />
        ) : (
          <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
            {activeCount > 0 && <PulseDot />}
            <span className="tabular-nums text-foreground">{activeCount}</span>
            <span>no ar</span>
            <span className="opacity-40">·</span>
            <span className="tabular-nums text-foreground">{fleet.length}</span>
            <span>monitorizadas</span>
          </p>
        )}
      </div>

      {/* Two-pane: list + map. Map on top on mobile, right on desktop. */}
      <div className="mx-auto w-full max-w-6xl min-h-0 flex-1 px-4 pb-4">
        <div className="flex h-full min-h-0 flex-col gap-4 md:flex-row">
          {/* Map */}
          <div className="relative order-1 h-[45vh] shrink-0 overflow-hidden rounded-2xl border border-black/5 shadow-sm dark:border-white/10 md:order-2 md:h-full md:min-h-0 md:flex-1 md:shrink">
            <ClientOnly fallback={<MapFallback />}>
              <AircraftMap
                aircraft={fleet}
                selectedIcao={selectedIcao}
                onSelect={setSelectedIcao}
                track={trackQuery.data}
                theme={theme}
              />
            </ClientOnly>
          </div>

          {/* List */}
          <div className="order-2 min-h-0 flex-1 overflow-y-auto rounded-2xl border border-black/5 bg-white/70 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60 md:order-1 md:h-full md:w-[380px] md:flex-none">
            {fleetQuery.isLoading ? (
              <ListSkeleton />
            ) : sorted.length === 0 ? (
              <div className="flex h-full flex-col items-center justify-center gap-2 px-6 py-16 text-center">
                <Plane className="size-6 text-muted-foreground/60" aria-hidden />
                <p className="text-sm text-muted-foreground">
                  Nenhuma aeronave monitorizada de momento.
                </p>
              </div>
            ) : (
              <ul className="divide-y divide-border/50">
                {sorted.map((a) => (
                  <AircraftRow
                    key={a.tracked.icao}
                    aircraft={a}
                    selected={a.tracked.icao === selectedIcao}
                    incident={
                      a.currentIncidentId
                        ? (incidentsById.get(a.currentIncidentId) ?? null)
                        : null
                    }
                    onSelect={() => select(a.tracked.icao)}
                  />
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

// ── Row ──────────────────────────────────────────────────────────────────────

function AircraftRow({
  aircraft,
  selected,
  incident,
  onSelect,
}: {
  aircraft: FleetAircraft
  selected: boolean
  incident: IncidentListItem | null
  onSelect: () => void
}) {
  const Icon = isHelicopter(aircraft.tracked.kind) ? Helicopter : Plane
  const title = aircraftLabel(aircraft)
  const subtitle = aircraftSubtitle(aircraft)
  const seen = aircraft.position
    ? `visto ${formatRelative(aircraft.position.sampledAt)}`
    : 'Sem posição recente'

  return (
    <li>
      <button
        type="button"
        onClick={onSelect}
        aria-pressed={selected}
        className={`flex w-full items-center gap-3 px-4 py-3 text-left transition-colors ${
          selected
            ? 'bg-blue-500/10'
            : 'hover:bg-black/[0.03] dark:hover:bg-white/[0.04]'
        }`}
      >
        <StatusDot active={aircraft.active} />
        <div className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-muted/60">
          <Icon className="size-4 text-muted-foreground" aria-hidden />
        </div>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-foreground">{title}</p>
          {subtitle && (
            <p className="truncate text-xs text-muted-foreground">{subtitle}</p>
          )}
          <p className="mt-0.5 flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-muted-foreground">
            <span>{seen}</span>
            {aircraft.currentIncidentId && (
              <IncidentChip
                incidentId={aircraft.currentIncidentId}
                incident={incident}
              />
            )}
          </p>
        </div>
      </button>
    </li>
  )
}

function IncidentChip({
  incidentId,
  incident,
}: {
  incidentId: string
  incident: IncidentListItem | null
}) {
  const place =
    incident &&
    (locationParts(null, incident.concelho, incident.district) ||
      incident.location)

  return (
    <Link
      to="/"
      search={{ incident: incidentId }}
      viewTransition
      onClick={(e) => e.stopPropagation()}
      className="inline-flex items-center gap-1 rounded-full bg-red-500/10 px-2 py-0.5 text-xs font-medium text-red-700 transition-colors hover:bg-red-500/20 dark:text-red-300"
    >
      <Plane className="size-3" aria-hidden />
      {place ? `No incêndio em ${place}` : 'Ver incêndio'}
    </Link>
  )
}

// ── Bits ─────────────────────────────────────────────────────────────────────

/** Live-status dot: pulsing green when active, static gray when idle. */
function StatusDot({ active }: { active: boolean }) {
  if (!active) {
    return (
      <span
        aria-hidden
        className="size-2.5 shrink-0 rounded-full bg-zinc-300 dark:bg-zinc-600"
      />
    )
  }
  return (
    <span className="relative flex size-2.5 shrink-0" aria-hidden>
      <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-500 opacity-75" />
      <span className="relative inline-flex size-2.5 rounded-full bg-green-500" />
    </span>
  )
}

/** The pulsing green dot next to the "no ar" count in the header. */
function PulseDot() {
  return (
    <span className="relative flex size-2" aria-hidden>
      <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-500 opacity-75" />
      <span className="relative inline-flex size-2 rounded-full bg-green-500" />
    </span>
  )
}

function MapFallback() {
  return (
    <div className="flex h-full w-full items-center justify-center bg-zinc-100 dark:bg-zinc-900">
      <Loader2 className="size-6 animate-spin text-zinc-400 dark:text-zinc-600" />
    </div>
  )
}

function ListSkeleton() {
  return (
    <ul className="divide-y divide-border/50">
      {Array.from({ length: 8 }).map((_, i) => (
        <li key={i} className="flex animate-pulse items-center gap-3 px-4 py-3">
          <span className="size-2.5 shrink-0 rounded-full bg-muted" />
          <span className="size-9 shrink-0 rounded-xl bg-muted" />
          <div className="flex-1 space-y-1.5">
            <span className="block h-3.5 w-32 rounded bg-muted" />
            <span className="block h-3 w-20 rounded bg-muted" />
          </div>
        </li>
      ))}
    </ul>
  )
}
