import { useCallback, useEffect, useMemo, useState } from 'react'
import { ClientOnly, createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'

import {
  activeIncidentsQuery,
  incidentQuery,
  recentIncidentsQuery,
} from '#/lib/fogos/api.ts'
import {
  STATUS_BUCKETS,
  WINDOW_HOURS,
  isOngoingStatus,
  statusBucket,
} from '#/lib/fogos/format.ts'
import type { StatusBucket } from '#/lib/fogos/format.ts'
import { normalizeIncidentParam } from '#/lib/fogos/search.ts'
import type { IndexSearch } from '#/lib/fogos/search.ts'
import type { IncidentListItem } from '#/lib/fogos/types.ts'
import { weatherAvailabilityOptions } from '#/lib/weather/api.ts'
import { radarFramesOptions, useRadarAnimation } from '#/lib/weather/radar.ts'
import { windFieldOptions } from '#/lib/weather/wind.ts'
import type { WeatherLayerKey } from '#/lib/weather/catalog.ts'
import { useTheme } from '#/lib/theme.ts'
import { AppToolbar } from '#/components/app-toolbar.tsx'
import { FireFilterControl } from '#/components/fire-filter-control.tsx'
import { FireMap } from '#/components/fire-map.tsx'
import type { IncidentMapOverlays } from '#/components/fire-map.tsx'
import { IncidentPanel } from '#/components/incident-panel.tsx'
import { MapLegend } from '#/components/map-legend.tsx'
import { WeatherLayerControl } from '#/components/weather-layer-control.tsx'

/** The two active buckets that the "Só ativos" shortcut maps to. */
const ACTIVE_BUCKETS: StatusBucket[] = ['dispatch', 'ongoing']

function bucketsEqual(
  a: ReadonlySet<StatusBucket>,
  b: readonly StatusBucket[],
): boolean {
  return a.size === b.length && b.every((x) => a.has(x))
}

export const Route = createFileRoute('/')({
  component: Home,
  // `?incident=ID` preselects an incident (used by concelho-page / alert links).
  validateSearch: (search: Record<string, unknown>): IndexSearch => ({
    incident: normalizeIncidentParam(search.incident),
  }),
  // Swallow prefetch failures: with the API down (or an older schema) the
  // page still renders and the client-side queries surface the error toast.
  loader: ({ context }) =>
    Promise.all([
      context.queryClient.ensureQueryData(activeIncidentsQuery()),
      context.queryClient.ensureQueryData(recentIncidentsQuery()),
    ]).catch(() => null),
})

function MapFallback() {
  return (
    <div className="flex h-full w-full items-center justify-center bg-zinc-100 dark:bg-zinc-900">
      <Loader2 className="size-6 animate-spin text-zinc-400 dark:text-zinc-600" />
    </div>
  )
}

function Home() {
  const theme = useTheme()
  // Filter state (ephemeral, not in the URL — same as the weather layer choice).
  const [buckets, setBuckets] = useState<Set<StatusBucket>>(
    () => new Set(STATUS_BUCKETS),
  )
  const [maxAgeHours, setMaxAgeHours] = useState<number | null>(null)
  // Only one map panel (layers | filters) is open at a time.
  const [openPanel, setOpenPanel] = useState<'layers' | 'filters' | null>(null)
  const [weatherLayer, setWeatherLayer] = useState<WeatherLayerKey | 'none'>(
    'none',
  )
  const [radarPlaying, setRadarPlaying] = useState(true)

  // "Só ativos" is a shortcut over the same bucket state: pressed exactly when
  // the selection is {dispatch, ongoing}. Toggling on narrows to those two;
  // toggling off restores all five.
  const activeOnly = bucketsEqual(buckets, ACTIVE_BUCKETS)
  const setActiveOnly = useCallback((next: boolean) => {
    setBuckets(new Set(next ? ACTIVE_BUCKETS : STATUS_BUCKETS))
  }, [])

  // Lock the document to the viewport while the full-screen map is mounted, and
  // release it on unmount so the content routes (estatísticas, concelho, sobre…)
  // scroll normally. See `body.map-locked` in styles.css.
  useEffect(() => {
    document.body.classList.add('map-locked')
    return () => document.body.classList.remove('map-locked')
  }, [])

  const active = useQuery(activeIncidentsQuery())
  const recent = useQuery(recentIncidentsQuery())
  const weatherAvailability = useQuery(weatherAvailabilityOptions())
  const radar = useQuery(radarFramesOptions())
  const wind = useQuery(windFieldOptions({ enabled: weatherLayer === 'wind' }))

  const radarFrames = radar.data?.frames ?? []
  const radarActiveIndex = useRadarAnimation(
    radarFrames,
    weatherLayer === 'radar' && radarPlaying,
  )

  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  // Selection is derived from the URL (?incident=ID) — a single source of truth,
  // so shared/concelho/alert links preselect and every selection is shareable.
  const selectedId = search.incident ?? null

  // Selecting or deselecting a fire PUSHES the new URL (Back deselects). The
  // guard drops redundant pushes (map-background clicks with nothing selected,
  // re-selecting the same fire) which also prevents navigation loops since the
  // handler only ever fires from user events, never from a selectedId effect.
  const handleSelect = useCallback(
    (id: string | null) => {
      const next = id ?? undefined
      if ((search.incident ?? undefined) === next) return
      void navigate({ search: (prev) => ({ ...prev, incident: next }) })
    },
    [navigate, search.incident],
  )

  // Per-incident map overlays (hotspots / perimeter / photos) are derived in the
  // panel and lifted here so FireMap can render them (mirrors the weather props).
  const [incidentOverlays, setIncidentOverlays] =
    useState<IncidentMapOverlays | null>(null)
  // Clear overlays immediately when the selection changes; the panel repopulates.
  useEffect(() => {
    setIncidentOverlays(null)
  }, [selectedId])

  const activeList = active.data ?? []

  // Base view: ongoing fires (active/em resolução/vigilância) always show,
  // whatever their age; finished ones only while their last change is within
  // the window. The activeIncidents version wins the dedup.
  const baseList = useMemo<IncidentListItem[]>(() => {
    const cutoff = Date.now() - WINDOW_HOURS * 60 * 60 * 1000
    const byId = new Map<string, IncidentListItem>()
    for (const inc of recent.data ?? []) {
      // Finished fires are windowed by their last change (updatedAt), matching
      // the server-side updatedAfter fetch but on a tighter rolling window.
      // Enrichment (ICNF, weather) counts as activity here on purpose, so a
      // late-enriched concluded fire can briefly resurface (owner's choice).
      const keep =
        isOngoingStatus(inc.status.code) || Date.parse(inc.updatedAt) >= cutoff
      if (keep) byId.set(inc.id, inc)
    }
    for (const inc of activeList) byId.set(inc.id, inc)
    return [...byId.values()]
  }, [activeList, recent.data])

  // Apply the map filters (status bucket + activity window). Computed against
  // Date.now() at render — fine since the data refetches every 60s.
  const list = useMemo<IncidentListItem[]>(() => {
    const now = Date.now()
    return baseList.filter((inc) => {
      if (!buckets.has(statusBucket(inc.status.code))) return false
      if (
        maxAgeHours != null &&
        now - Date.parse(inc.updatedAt) > maxAgeHours * 3_600_000
      ) {
        return false
      }
      return true
    })
  }, [baseList, buckets, maxAgeHours])

  const selectedInList = useMemo(
    () => list.find((i) => i.id === selectedId) ?? null,
    [list, selectedId],
  )

  // Deep links (?incident=ID) can point at a concluded/older fire outside the
  // loaded window — fetch its detail directly and synthesize a list-item shape
  // so the panel opens. Only runs when the selection isn't already in the list;
  // an unknown/bogus id resolves to null and degrades silently (no panel).
  const detailFallback = useQuery({
    ...incidentQuery(selectedId ?? ''),
    enabled: selectedId != null && selectedInList == null,
  })
  const selectedFromDetail = useMemo<IncidentListItem | null>(() => {
    const d = detailFallback.data
    if (!d) return null
    return {
      id: d.id,
      location: d.location,
      district: d.district,
      concelho: d.concelho,
      freguesia: d.freguesia,
      coordinates: d.coordinates,
      status: d.status,
      kind: d.kind,
      natureza: d.natureza,
      important: d.important,
      occurredAt: d.occurredAt,
      updatedAt: d.updatedAt,
      statusChangedAt: null,
      resources: {
        man: d.resources.man,
        terrain: d.resources.terrain,
        aerial: d.resources.aerial,
        aquatic: d.resources.aquatic,
      },
      signals: {
        escalating: d.signals.escalating,
        rekindle: d.signals.rekindle,
        criticalConditions: d.signals.criticalConditions,
      },
    }
  }, [detailFallback.data])

  const selectedIncident = selectedInList ?? selectedFromDetail

  // Keep showing whatever we have while a refetch is in flight; only fall
  // back to the count skeleton when there is nothing to show yet. Both feeds
  // are always needed now that filtering runs over the merged base list.
  const stillLoading = active.isLoading || recent.isLoading
  const showSkeleton = stillLoading && list.length === 0
  const isError = active.isError || recent.isError
  const showEmpty = !stillLoading && !isError && list.length === 0

  return (
    <main className="relative h-[100dvh] w-full overflow-hidden">
      <ClientOnly fallback={<MapFallback />}>
        <FireMap
          incidents={list}
          selectedId={selectedId}
          selectedCoordinates={selectedIncident?.coordinates ?? null}
          onSelect={handleSelect}
          theme={theme}
          weatherLayer={weatherLayer}
          weatherAvailability={weatherAvailability.data}
          radarData={radar.data}
          radarActiveIndex={radarActiveIndex}
          windFields={wind.data}
          incidentOverlays={incidentOverlays}
        />
      </ClientOnly>

      <div className="pointer-events-none absolute inset-0 z-30">
        <AppToolbar
          variant="map"
          count={list.length}
          isLoading={showSkeleton}
          activeOnly={activeOnly}
          onActiveOnlyChange={setActiveOnly}
          showActiveOnly
          rightHidden={!!selectedIncident}
          rightSlot={
            <>
              <WeatherLayerControl
                value={weatherLayer}
                onChange={setWeatherLayer}
                availability={weatherAvailability.data}
                radarPlaying={radarPlaying}
                onToggleRadarPlaying={() => setRadarPlaying((p) => !p)}
                radarActiveFrame={radarFrames[radarActiveIndex]}
                open={openPanel === 'layers'}
                onOpenChange={(o) => setOpenPanel(o ? 'layers' : null)}
              />
              <FireFilterControl
                open={openPanel === 'filters'}
                onOpenChange={(o) => setOpenPanel(o ? 'filters' : null)}
                buckets={buckets}
                onBucketsChange={setBuckets}
                maxAgeHours={maxAgeHours}
                onMaxAgeChange={setMaxAgeHours}
                visibleCount={list.length}
                totalCount={baseList.length}
              />
            </>
          }
        />

        <div className="pointer-events-auto absolute bottom-6 left-4 z-10">
          <MapLegend />
        </div>

        {(isError || showEmpty) && (
          <div className="pointer-events-auto absolute bottom-6 left-1/2 -translate-x-1/2">
            <div className="rounded-full border border-black/5 bg-white/75 px-4 py-2 text-sm font-medium text-zinc-700 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70 dark:text-zinc-200">
              {isError
                ? 'Não foi possível carregar os dados. A tentar novamente…'
                : activeOnly
                  ? 'Sem incêndios ativos neste momento.'
                  : 'Sem incêndios neste momento.'}
            </div>
          </div>
        )}
      </div>

      <div className="pointer-events-auto">
        <IncidentPanel
          incident={selectedIncident}
          onClose={() => handleSelect(null)}
          onOverlaysChange={setIncidentOverlays}
        />
      </div>
    </main>
  )
}
