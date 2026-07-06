import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { AnimatePresence, motion } from 'motion/react'
import {
  Droplets,
  Flame,
  Helicopter,
  Navigation,
  Pause,
  Plane,
  Play,
  Ship,
  Thermometer,
  ThermometerSun,
  TrendingUp,
  TriangleAlert,
  Truck,
  Users,
  Wind,
  X,
} from 'lucide-react'

import { ScrollArea } from '#/components/ui/scroll-area.tsx'
import { Drawer, DrawerContent } from '#/components/ui/drawer.tsx'
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '#/components/ui/tooltip.tsx'
import { ResourceChart } from '#/components/resource-chart.tsx'
import type { IncidentMapOverlays } from '#/components/fire-map.tsx'
import { useIsMobile } from '#/lib/use-is-mobile.ts'
import { incidentQuery, kmlVersionQuery } from '#/lib/fogos/api.ts'
import {
  badgeNeedsDarkText,
  compassBearing,
  criticalReasonLabel,
  formatAbsolute,
  formatDuration,
  formatHectares,
  formatRelative,
  formatTimelineStamp,
  hasResource,
  incidentTitle,
  locationParts,
  statusColorForCode,
} from '#/lib/fogos/format.ts'
import {
  hotspotTimeRange,
  hotspotsAtTime,
  mergeHotspots,
} from '#/lib/fogos/hotspots.ts'
import { parseKmlToGeoJson } from '#/lib/fogos/kml.ts'
import { concelhoByName } from '#/lib/fogos/concelhos.ts'
import type {
  IncidentAircraft,
  IncidentDetail,
  IncidentListItem,
  ResponseTimes,
} from '#/lib/fogos/types.ts'

interface IncidentPanelProps {
  incident: IncidentListItem | null
  onClose: () => void
  onOverlaysChange: (overlays: IncidentMapOverlays | null) => void
}

export function IncidentPanel({
  incident,
  onClose,
  onOverlaysChange,
}: IncidentPanelProps) {
  const isMobile = useIsMobile()

  // Close on Escape (desktop card; the drawer handles it natively).
  useEffect(() => {
    if (!incident || isMobile) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [incident, isMobile, onClose])

  if (isMobile) {
    return (
      <Drawer
        open={incident != null}
        onOpenChange={(open) => {
          if (!open) onClose()
        }}
        snapPoints={[0.55, 1]}
      >
        <DrawerContent className="max-h-[96vh] border-black/5 bg-white/85 backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/85">
          {incident && (
            // Native scroll container (not radix ScrollArea): vaul detects a real
            // overflow-y ancestor to decide between scrolling the content and
            // dragging the sheet. At the top snap point this scrolls; at scrollTop
            // 0 a downward drag still dismisses. `overscroll-contain` stops the
            // gesture from chaining to the page behind.
            <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain [-webkit-overflow-scrolling:touch]">
              <PanelContent
                incident={incident}
                onClose={onClose}
                onOverlaysChange={onOverlaysChange}
              />
            </div>
          )}
        </DrawerContent>
      </Drawer>
    )
  }

  return (
    <AnimatePresence>
      {incident && (
        <motion.aside
          key={incident.id}
          initial={{ x: 40, opacity: 0 }}
          animate={{ x: 0, opacity: 1 }}
          exit={{ x: 40, opacity: 0 }}
          transition={{ type: 'spring', stiffness: 260, damping: 30 }}
          className="absolute right-4 top-4 bottom-4 z-20 flex w-[400px] max-w-[calc(100vw-2rem)] flex-col overflow-hidden rounded-2xl border border-black/5 bg-white/75 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70"
        >
          <ScrollArea className="min-h-0 flex-1">
            <PanelContent
              incident={incident}
              onClose={onClose}
              onOverlaysChange={onOverlaysChange}
            />
          </ScrollArea>
        </motion.aside>
      )}
    </AnimatePresence>
  )
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h3 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
      {children}
    </h3>
  )
}

/**
 * Location line (freguesia · concelho · distrito) with the concelho linked to
 * its profile page. Mirrors `locationParts` dedup; falls back to plain text when
 * no DICO resolves (handled by the caller).
 */
function PlaceLine({
  freguesia,
  concelho,
  district,
  dico,
}: {
  freguesia: string | null
  concelho: string | null
  district: string | null
  dico: string
}) {
  const seen = new Set<string>()
  const parts: Array<{ value: string; isConcelho: boolean }> = []
  for (const raw of [freguesia, concelho, district]) {
    const value = raw?.trim()
    if (!value) continue
    const key = value.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    parts.push({ value, isConcelho: raw === concelho })
  }
  if (parts.length === 0) return null

  return (
    <p className="text-sm text-muted-foreground">
      {parts.map((part, i) => (
        <Fragment key={part.value}>
          {i > 0 && <span className="mx-1 opacity-50">·</span>}
          {part.isConcelho ? (
            <Link
              to="/concelho/$dico"
              params={{ dico }}
              viewTransition
              className="font-medium text-foreground underline-offset-2 hover:underline"
            >
              {part.value}
            </Link>
          ) : (
            part.value
          )}
        </Fragment>
      ))}
    </p>
  )
}

function PanelContent({
  incident,
  onClose,
  onOverlaysChange,
}: {
  incident: IncidentListItem
  onClose: () => void
  onOverlaysChange: (overlays: IncidentMapOverlays | null) => void
}) {
  const { data: detail } = useQuery({
    ...incidentQuery(incident.id),
    enabled: true,
  })

  // --- Spread scrubber (Propagação) --------------------------------------
  const hotspotPoints = useMemo(
    () => mergeHotspots(detail?.hotspots),
    [detail?.hotspots],
  )
  const range = useMemo(() => hotspotTimeRange(hotspotPoints), [hotspotPoints])
  const [scrubTime, setScrubTime] = useState<number | null>(null)
  const [playing, setPlaying] = useState(false)

  // Snap the scrubber to the latest hotspot whenever the range changes.
  useEffect(() => {
    setScrubTime(range ? range.end : null)
    setPlaying(false)
  }, [range?.start, range?.end])

  // Play loop: sweep first → last over a fixed duration, then stop.
  useEffect(() => {
    if (!playing || !range) return
    const SWEEP_MS = 6000
    const speed = (range.end - range.start) / SWEEP_MS
    let frame = 0
    let last = performance.now()
    const step = (now: number) => {
      const dt = now - last
      last = now
      setScrubTime((prev) => {
        const nextValue = (prev ?? range.start) + dt * speed
        if (nextValue >= range.end) {
          setPlaying(false)
          return range.end
        }
        return nextValue
      })
      frame = requestAnimationFrame(step)
    }
    frame = requestAnimationFrame(step)
    return () => cancelAnimationFrame(frame)
  }, [playing, range?.start, range?.end])

  const visibleHotspots = useMemo(() => {
    if (!range || scrubTime == null) return []
    return hotspotsAtTime(hotspotPoints, scrubTime, range)
  }, [hotspotPoints, scrubTime, range])

  // --- Perimeter versions (Perímetro) ------------------------------------
  const versions = useMemo(
    () =>
      [...(detail?.kmlHistory ?? [])].sort(
        (a, b) => Date.parse(b.capturedAt) - Date.parse(a.capturedAt),
      ),
    [detail?.kmlHistory],
  )
  const [selectedVersionId, setSelectedVersionId] = useState<string | null>(null)
  useEffect(() => {
    setSelectedVersionId(versions[0]?.id ?? null)
  }, [detail?.id, versions.length])

  const { data: kmlText } = useQuery({
    ...kmlVersionQuery(incident.id, selectedVersionId ?? ''),
    enabled: selectedVersionId != null,
  })
  const perimeter = useMemo(
    () => (kmlText ? parseKmlToGeoJson(kmlText) : null),
    [kmlText],
  )

  // --- Geotagged photos on the map ---------------------------------------
  const mapPhotos = useMemo(
    () =>
      (detail?.photos ?? [])
        .filter((p) => p.gps != null)
        .map((p) => ({
          id: p.id,
          lng: p.gps!.longitude,
          lat: p.gps!.latitude,
          url: p.publicUrl,
        })),
    [detail?.photos],
  )

  // Bundle overlays and thread them up to the map (see index.tsx).
  const overlays = useMemo<IncidentMapOverlays>(
    () => ({
      hotspots: visibleHotspots.map((h) => ({
        id: h.id,
        lng: h.lng,
        lat: h.lat,
        recency: h.recency,
      })),
      perimeter,
      photos: mapPhotos,
    }),
    [visibleHotspots, perimeter, mapPhotos],
  )
  useEffect(() => {
    onOverlaysChange(overlays)
  }, [overlays, onOverlaysChange])
  // Overlay clearing on deselect/switch is owned by the index route; an unmount
  // cleanup here would fire after AnimatePresence exit and wipe the next
  // incident's overlays.

  // Render instantly from the list item; enrich when the detail lands.
  const base = detail ?? incident
  const status = base.status
  const badgeColor = statusColorForCode(status.code)
  const darkText = badgeNeedsDarkText(badgeColor)

  const place = locationParts(base.freguesia, base.concelho, base.district)
  const concelhoEntry = base.concelho ? concelhoByName(base.concelho) : null

  return (
    <div className="flex flex-col gap-5 p-4">
      {/* Header row */}
      <div className="flex items-start gap-3">
        <div className="flex-1">
          <span
            className="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold"
            style={{
              backgroundColor: badgeColor,
              color: darkText ? '#18181b' : '#ffffff',
            }}
          >
            {status.label}
          </span>
          <h2 className="mt-2 text-lg font-semibold leading-tight text-foreground">
            {incidentTitle(base)}
          </h2>
          <SignalBadges
            escalating={base.signals.escalating}
            rekindle={base.signals.rekindle}
            criticalConditions={base.signals.criticalConditions}
            reasons={detail?.signals.criticalReasons ?? []}
          />
        </div>
        <button
          type="button"
          onClick={onClose}
          aria-label="Fechar detalhes"
          className="flex size-8 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-black/5 dark:hover:bg-white/10"
        >
          <X className="size-4" />
        </button>
      </div>

      {/* Location block */}
      <div className="space-y-1">
        <p className="font-medium text-foreground">{base.location}</p>
        {place &&
          (concelhoEntry ? (
            <PlaceLine
              freguesia={base.freguesia}
              concelho={base.concelho}
              district={base.district}
              dico={concelhoEntry.dico}
            />
          ) : (
            <p className="text-sm text-muted-foreground">{place}</p>
          ))}
        <p className="text-sm text-muted-foreground">
          {formatRelative(base.occurredAt)}
          <span className="mx-1.5 opacity-40">·</span>
          {formatAbsolute(base.occurredAt)}
        </p>
        <p className="text-xs text-muted-foreground">
          Atualizado {formatRelative(base.updatedAt)}
        </p>
      </div>

      {/* Important banner */}
      {base.important && (
        <div className="flex items-center gap-2 rounded-xl border border-amber-500/30 bg-amber-500/15 px-3 py-2 text-sm font-medium text-amber-700 dark:text-amber-300">
          <TriangleAlert className="size-4 shrink-0" />
          Ocorrência importante
        </div>
      )}

      <ResourcesSection resources={base.resources} />

      {detail && (
        <ResourceChart
          history={detail.history}
          current={{
            at: detail.updatedAt,
            man: detail.resources.man,
            terrain: detail.resources.terrain,
            aerial: detail.resources.aerial,
          }}
        />
      )}

      {detail && <AircraftSection aircraft={detail.aircraft} />}

      {detail?.weather && <WeatherSection weather={detail.weather} />}

      {range && scrubTime != null && (
        <SpreadSection
          range={range}
          scrubTime={scrubTime}
          visibleCount={visibleHotspots.length}
          totalCount={hotspotPoints.length}
          playing={playing}
          onScrub={(value) => {
            setPlaying(false)
            setScrubTime(value)
          }}
          onTogglePlay={() => {
            setPlaying((p) => {
              if (!p && scrubTime >= range.end) setScrubTime(range.start)
              return !p
            })
          }}
          weather={detail?.weather ?? null}
        />
      )}

      {detail && versions.length > 0 && (
        <PerimeterSection
          versions={versions}
          selectedId={selectedVersionId}
          onSelect={setSelectedVersionId}
        />
      )}

      {detail?.icnf && <IcnfSection icnf={detail.icnf} />}

      {detail && (
        <EvolutionSection
          history={detail.statusHistory}
          responseTimes={detail.responseTimes}
        />
      )}

      {detail && detail.photos.length > 0 && (
        <PhotosSection photos={detail.photos} />
      )}
    </div>
  )
}

const CRITICAL_RED = '#991b1b'

function SignalBadges({
  escalating,
  rekindle,
  criticalConditions,
  reasons,
}: {
  escalating: boolean
  rekindle: boolean
  criticalConditions: boolean
  reasons: string[]
}) {
  if (!escalating && !rekindle && !criticalConditions) return null

  return (
    <div className="mt-2 flex flex-wrap items-center gap-1.5">
      {escalating && (
        <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/15 px-2 py-0.5 text-xs font-semibold text-amber-700 dark:bg-amber-500/20 dark:text-amber-300">
          <TrendingUp className="size-3" aria-hidden />
          Em escalada
        </span>
      )}
      {rekindle && (
        <span className="inline-flex items-center gap-1 rounded-full bg-red-500/15 px-2 py-0.5 text-xs font-semibold text-red-700 dark:bg-red-500/20 dark:text-red-300">
          <Flame className="size-3" aria-hidden />
          Reacendimento
        </span>
      )}
      {criticalConditions && (
        <TooltipProvider>
          <Tooltip>
            <TooltipTrigger asChild>
              <span
                className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold text-white"
                style={{ backgroundColor: CRITICAL_RED }}
              >
                <ThermometerSun className="size-3" aria-hidden />
                Condições críticas
              </span>
            </TooltipTrigger>
            {reasons.length > 0 && (
              <TooltipContent side="bottom">
                <ul className="space-y-0.5">
                  {reasons.map((key) => (
                    <li key={key}>{criticalReasonLabel(key)}</li>
                  ))}
                </ul>
              </TooltipContent>
            )}
          </Tooltip>
        </TooltipProvider>
      )}
    </div>
  )
}

const RESOURCE_TILES = [
  { key: 'man', label: 'Operacionais', Icon: Users },
  { key: 'terrain', label: 'Meios terrestres', Icon: Truck },
  { key: 'aerial', label: 'Meios aéreos', Icon: Plane },
  { key: 'aquatic', label: 'Meios aquáticos', Icon: Ship },
] as const

function ResourcesSection({
  resources,
}: {
  resources: Pick<
    IncidentDetail['resources'],
    'man' | 'terrain' | 'aerial' | 'aquatic'
  >
}) {
  // Man/terrain/aerial always render — 0 is real data ("no aerial means
  // committed"), not absence. Aquatic stays conditional: it is almost always 0.
  const tiles = RESOURCE_TILES.filter(
    (t) => t.key !== 'aquatic' || hasResource(resources.aquatic),
  )
  // ANEPC publishes -1 while means are undisclosed.
  const allUnknown = tiles.every(({ key }) => resources[key] < 0)

  return (
    <section className="space-y-2">
      <SectionTitle>Meios no terreno</SectionTitle>
      <div
        className={
          tiles.length === 4 ? 'grid grid-cols-2 gap-2' : 'grid grid-cols-3 gap-2'
        }
      >
        {tiles.map(({ key, label, Icon }) => (
          <div key={key} className="rounded-xl bg-muted/60 p-3">
            <Icon className="size-4 text-muted-foreground" />
            <div className="mt-1.5 text-2xl font-bold tabular-nums text-foreground">
              {resources[key] < 0 ? '—' : resources[key]}
            </div>
            <div className="text-xs text-muted-foreground">{label}</div>
          </div>
        ))}
      </div>
      {allUnknown && (
        <p className="text-xs text-muted-foreground">
          Sem informação de meios divulgada pela ANEPC.
        </p>
      )}
    </section>
  )
}

function WeatherSection({
  weather,
}: {
  weather: NonNullable<IncidentDetail['weather']>
}) {
  // Individual readings are nullable — a station can miss any sensor.
  const items = [
    {
      Icon: Thermometer,
      label: 'Temperatura',
      value:
        weather.temperature != null
          ? `${Math.round(weather.temperature)}°`
          : null,
    },
    {
      Icon: Droplets,
      label: 'Humidade',
      value:
        weather.humidity != null ? `${Math.round(weather.humidity)}%` : null,
    },
    {
      Icon: Wind,
      label: 'Vento',
      value:
        weather.windSpeedKmh != null
          ? `${Math.round(weather.windSpeedKmh)} km/h`
          : null,
    },
  ].filter((item) => item.value != null)

  if (items.length === 0) return null

  return (
    <section className="space-y-2">
      <SectionTitle>Meteorologia</SectionTitle>
      <div className="flex items-center gap-4">
        {items.map(({ Icon, label, value }) => (
          <div key={label} className="flex items-center gap-1.5" title={label}>
            <Icon className="size-4 text-muted-foreground" aria-hidden />
            <span className="sr-only">{label}</span>
            <span className="text-sm font-medium tabular-nums text-foreground">
              {value}
            </span>
          </div>
        ))}
      </div>
      <p className="text-xs text-muted-foreground">
        Estação {weather.stationName} · {weather.distanceKm.toFixed(1)} km
      </p>
    </section>
  )
}

function IcnfSection({
  icnf,
}: {
  icnf: NonNullable<IncidentDetail['icnf']>
}) {
  const hasContent =
    icnf.causeType != null ||
    icnf.cause != null ||
    icnf.burnArea?.total != null
  if (!hasContent) return null

  return (
    <section className="space-y-2">
      <SectionTitle>Causa e área ardida</SectionTitle>
      <dl className="space-y-1 text-sm">
        {icnf.causeType && (
          <div className="flex justify-between gap-4">
            <dt className="text-muted-foreground">Tipo de causa</dt>
            <dd className="text-right font-medium text-foreground">
              {icnf.causeType}
            </dd>
          </div>
        )}
        {icnf.cause && (
          <div className="flex justify-between gap-4">
            <dt className="text-muted-foreground">Causa</dt>
            <dd className="text-right font-medium text-foreground">
              {icnf.cause}
            </dd>
          </div>
        )}
        {icnf.burnArea?.total != null && (
          <div className="flex justify-between gap-4">
            <dt className="text-muted-foreground">Área ardida</dt>
            <dd className="text-right font-medium tabular-nums text-foreground">
              {formatHectares(icnf.burnArea.total)}
            </dd>
          </div>
        )}
      </dl>
    </section>
  )
}

const RESPONSE_TIME_TILES = [
  { key: 'dispatchToArrivalSeconds', label: 'Despacho → Chegada' },
  { key: 'arrivalToControlSeconds', label: 'Chegada → Resolução' },
  { key: 'totalSeconds', label: 'Total' },
] as const

function ResponseTimesRow({ responseTimes }: { responseTimes: ResponseTimes }) {
  const tiles = RESPONSE_TIME_TILES.filter(
    (t) => responseTimes[t.key] != null,
  ).map((t) => ({ label: t.label, value: responseTimes[t.key] as number }))

  if (tiles.length === 0) return null

  return (
    <div className="grid grid-cols-3 gap-2">
      {tiles.map(({ label, value }) => (
        <div key={label} className="rounded-xl bg-muted/60 p-3">
          <div className="text-sm font-bold tabular-nums text-foreground">
            {formatDuration(value)}
          </div>
          <div className="mt-0.5 text-[11px] leading-tight text-muted-foreground">
            {label}
          </div>
        </div>
      ))}
    </div>
  )
}

function hasAnyResponseTime(rt: ResponseTimes): boolean {
  return (
    rt.dispatchToArrivalSeconds != null ||
    rt.arrivalToControlSeconds != null ||
    rt.totalSeconds != null
  )
}

function EvolutionSection({
  history,
  responseTimes,
}: {
  history: IncidentDetail['statusHistory']
  responseTimes: ResponseTimes | null
}) {
  const showResponse = responseTimes != null && hasAnyResponseTime(responseTimes)
  if (history.length === 0 && !showResponse) return null

  const ordered = [...history].sort(
    (a, b) => Date.parse(b.at) - Date.parse(a.at),
  )
  return (
    <section className="space-y-3">
      <SectionTitle>Evolução</SectionTitle>
      {showResponse && <ResponseTimesRow responseTimes={responseTimes} />}
      {ordered.length > 0 && (
        <ol className="space-y-3">
          {ordered.map((entry, i) => (
            <li key={`${entry.at}-${i}`} className="flex items-start gap-3">
              <span
                className="mt-1 size-2.5 shrink-0 rounded-full"
                style={{ backgroundColor: statusColorForCode(entry.code) }}
              />
              <div className="min-w-0">
                <p className="text-sm font-medium text-foreground">
                  {entry.label}
                </p>
                <p className="text-xs text-muted-foreground">
                  {formatTimelineStamp(entry.at)}
                </p>
              </div>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

function isHelicopter(kind: string | null): boolean {
  const k = kind?.toLowerCase() ?? ''
  return k.includes('heli') || k.includes('rotor')
}

function AircraftSection({ aircraft }: { aircraft: IncidentAircraft[] }) {
  const active = aircraft.filter((a) => a.active)
  if (active.length === 0) return null

  return (
    <section className="space-y-2">
      <SectionTitle>Meios aéreos associados</SectionTitle>
      <ul className="space-y-2">
        {active.map((a) => {
          const Icon = isHelicopter(a.kind) ? Helicopter : Plane
          const title =
            a.name?.trim() || a.registration?.trim() || a.icao.toUpperCase()
          const subtitle =
            a.registration && a.registration !== title
              ? a.registration
              : a.kind
          return (
            <li key={a.icao} className="flex items-center gap-3">
              <div className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-muted/60">
                <Icon className="size-4 text-muted-foreground" aria-hidden />
              </div>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-foreground">
                  {title}
                </p>
                {subtitle && (
                  <p className="truncate text-xs text-muted-foreground">
                    {subtitle}
                  </p>
                )}
              </div>
              <span className="shrink-0 text-xs text-muted-foreground">
                {formatRelative(a.lastSeenAt)}
              </span>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

const scrubStampFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
  hour: '2-digit',
  minute: '2-digit',
})

function SpreadSection({
  range,
  scrubTime,
  visibleCount,
  totalCount,
  playing,
  onScrub,
  onTogglePlay,
  weather,
}: {
  range: { start: number; end: number }
  scrubTime: number
  visibleCount: number
  totalCount: number
  playing: boolean
  onScrub: (value: number) => void
  onTogglePlay: () => void
  weather: IncidentDetail['weather']
}) {
  const bearing =
    weather != null ? compassBearing(weather.windDirection) : null
  const showWind =
    weather != null &&
    (weather.windDirection != null || weather.windSpeedKmh != null)

  return (
    <section className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <SectionTitle>Propagação</SectionTitle>
        {showWind && (
          <span className="inline-flex items-center gap-1 rounded-full bg-muted/60 px-2 py-0.5 text-xs font-medium text-foreground">
            <Navigation
              className="size-3 text-muted-foreground"
              aria-hidden
              style={
                bearing != null
                  ? { transform: `rotate(${bearing}deg)` }
                  : undefined
              }
            />
            {weather?.windDirection ?? ''}
            {weather?.windSpeedKmh != null
              ? ` ${Math.round(weather.windSpeedKmh)} km/h`
              : ''}
          </span>
        )}
      </div>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={onTogglePlay}
          aria-label={playing ? 'Pausar' : 'Reproduzir'}
          className="flex size-8 shrink-0 items-center justify-center rounded-full bg-muted/60 text-foreground transition-colors hover:bg-muted"
        >
          {playing ? (
            <Pause className="size-4" />
          ) : (
            <Play className="size-4" />
          )}
        </button>
        <input
          type="range"
          min={range.start}
          max={range.end}
          step={Math.max(1, Math.round((range.end - range.start) / 200))}
          value={scrubTime}
          onChange={(e) => onScrub(Number(e.target.value))}
          aria-label="Momento da propagação"
          className="h-1.5 w-full cursor-pointer appearance-none rounded-full bg-muted accent-orange-500"
        />
      </div>
      <p className="text-xs text-muted-foreground">
        {scrubStampFmt.format(scrubTime)}
        <span className="mx-1.5 opacity-40">·</span>
        {visibleCount}/{totalCount} focos
      </p>
    </section>
  )
}

function PerimeterSection({
  versions,
  selectedId,
  onSelect,
}: {
  versions: IncidentDetail['kmlHistory']
  selectedId: string | null
  onSelect: (id: string) => void
}) {
  return (
    <section className="space-y-2">
      <SectionTitle>Perímetro</SectionTitle>
      <div className="flex flex-wrap gap-1.5">
        {versions.map((v) => {
          const selected = v.id === selectedId
          return (
            <button
              key={v.id}
              type="button"
              onClick={() => onSelect(v.id)}
              aria-pressed={selected}
              className={
                selected
                  ? 'rounded-full bg-orange-500/15 px-2.5 py-1 text-xs font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
                  : 'rounded-full bg-muted/60 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted'
              }
            >
              {formatTimelineStamp(v.capturedAt)}
              {v.vost ? ' · VOST' : ''}
            </button>
          )
        })}
      </div>
    </section>
  )
}

function PhotosSection({
  photos,
}: {
  photos: IncidentDetail['photos']
}) {
  return (
    <section className="space-y-2">
      <SectionTitle>Fotografias</SectionTitle>
      <div className="grid grid-cols-2 gap-2">
        {photos.map((photo) => (
          <img
            key={photo.id}
            src={photo.publicUrl}
            alt=""
            loading="lazy"
            className="aspect-video w-full rounded-lg object-cover"
          />
        ))}
      </div>
    </section>
  )
}
