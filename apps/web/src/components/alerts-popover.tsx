import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  Bell,
  BellRing,
  Loader2,
  MapPin,
  Search,
  Trash2,
} from 'lucide-react'

import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '#/components/ui/popover.tsx'
import { useToast } from '#/components/toast.tsx'
import {
  alertEventsQuery,
  createAlertSubscription,
  deleteAlertSubscription,
} from '#/lib/fogos/api.ts'
import {
  alertKindTitle,
  latestCreatedAt,
  newEvents,
  readSeenAt,
  readSubscriptionId,
  writeSeenAt,
  writeSubscriptionId,
} from '#/lib/fogos/alerts.ts'
import { formatRelative } from '#/lib/fogos/format.ts'
import { searchConcelhos } from '#/lib/fogos/concelhos.ts'
import type { ConcelhoEntry } from '#/lib/fogos/concelhos.ts'
import type { AlertEvent } from '#/lib/fogos/types.ts'

function notify(title: string, body: string): void {
  if (typeof Notification === 'undefined') return
  if (Notification.permission === 'granted') {
    try {
      new Notification(title, { body })
    } catch {
      // some browsers throw when constructed outside a SW context — ignore.
    }
  }
}

/**
 * "Alertas" button + popover. Stays mounted in the header so the 60s
 * `alertEvents` poll runs (and surfaces toasts + Notifications) regardless of
 * whether the popover is open. One anonymous subscription per device, in
 * localStorage.
 */
export function AlertsPopover({
  triggerClassName,
}: {
  triggerClassName?: string
} = {}) {
  const { toast } = useToast()
  const [subscriptionId, setSubscriptionId] = useState<string | null>(null)

  // Hydrate the persisted id after mount (SSR-safe).
  useEffect(() => {
    setSubscriptionId(readSubscriptionId())
  }, [])

  const eventsQuery = useQuery(alertEventsQuery(subscriptionId))
  const events = eventsQuery.data ?? []

  // Surface new events as toasts + Notifications. On the first fetch for a
  // device that already has history, initialise the cursor without toasting.
  const initialised = useRef(false)
  useEffect(() => {
    if (!subscriptionId || !eventsQuery.isSuccess) return
    const seenAt = readSeenAt()
    if (!initialised.current && seenAt == null) {
      initialised.current = true
      const newest = latestCreatedAt(events)
      if (newest) writeSeenAt(newest)
      return
    }
    initialised.current = true
    const fresh = newEvents(events, seenAt)
    if (fresh.length === 0) return
    for (const e of fresh) {
      const title = alertKindTitle(e.kind)
      toast({
        title,
        description: e.message,
        href: e.incidentId ? `/?incident=${e.incidentId}` : undefined,
      })
      notify(title, e.message)
    }
    const newest = latestCreatedAt(events)
    if (newest) writeSeenAt(newest)
  }, [subscriptionId, eventsQuery.isSuccess, eventsQuery.dataUpdatedAt, events, toast])

  // Reset the per-device cursor guard when the subscription changes.
  useEffect(() => {
    initialised.current = false
  }, [subscriptionId])

  const handleCreated = useCallback((id: string, createdAt: string) => {
    writeSubscriptionId(id)
    // Start the cursor at creation time — only future events toast.
    writeSeenAt(createdAt)
    setSubscriptionId(id)
  }, [])

  const handleDeleted = useCallback(() => {
    writeSubscriptionId(null)
    setSubscriptionId(null)
  }, [])

  const active = subscriptionId != null

  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label="Alertas"
          className={
            triggerClassName ??
            'relative flex h-10 items-center gap-2 rounded-full border border-black/5 bg-white/75 px-3.5 text-sm font-medium text-zinc-700 shadow-lg backdrop-blur-xl transition-colors hover:bg-white/90 dark:border-white/10 dark:bg-zinc-900/70 dark:text-zinc-200 dark:hover:bg-zinc-900/90'
          }
        >
          {active ? (
            <BellRing className="size-[18px]" />
          ) : (
            <Bell className="size-[18px]" />
          )}
          <span className="hidden sm:inline">Alertas</span>
          {active && (
            <span className="absolute right-2.5 top-2.5 size-2 rounded-full bg-orange-500 ring-2 ring-white dark:ring-zinc-900" />
          )}
        </button>
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80">
        {active ? (
          <ManageView
            subscriptionId={subscriptionId!}
            events={events}
            onDeleted={handleDeleted}
          />
        ) : (
          <CreateView onCreated={handleCreated} />
        )}
      </PopoverContent>
    </Popover>
  )
}

// ── Create ───────────────────────────────────────────────────────────────────

type Mode = 'concelho' | 'point'

function CreateView({
  onCreated,
}: {
  onCreated: (id: string, createdAt: string) => void
}) {
  const [mode, setMode] = useState<Mode>('concelho')
  const [query, setQuery] = useState('')
  const [chosen, setChosen] = useState<ConcelhoEntry | null>(null)
  const [radiusKm, setRadiusKm] = useState(10)
  const [point, setPoint] = useState<{ lat: number; lng: number } | null>(null)
  const [riskThreshold, setRiskThreshold] = useState<0 | 4 | 5>(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [locating, setLocating] = useState(false)

  const results = chosen ? [] : searchConcelhos(query, 6)

  const maybeAskNotifications = () => {
    if (typeof Notification !== 'undefined' && Notification.permission === 'default') {
      void Notification.requestPermission()
    }
  }

  const useMyLocation = () => {
    if (typeof navigator === 'undefined' || !navigator.geolocation) {
      setError('Geolocalização não disponível neste dispositivo.')
      return
    }
    setLocating(true)
    setError(null)
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setPoint({ lat: pos.coords.latitude, lng: pos.coords.longitude })
        setLocating(false)
      },
      () => {
        setError('Não foi possível obter a sua localização.')
        setLocating(false)
      },
      { enableHighAccuracy: true, timeout: 10_000 },
    )
  }

  const submit = async () => {
    setError(null)
    setBusy(true)
    try {
      maybeAskNotifications()
      const sub =
        mode === 'concelho'
          ? await createAlertSubscription({
              data: {
                kind: 'CONCELHO',
                dico: chosen!.dico,
                riskThreshold: riskThreshold || null,
              },
            })
          : await createAlertSubscription({
              data: {
                kind: 'POINT',
                latitude: point!.lat,
                longitude: point!.lng,
                radiusKm,
              },
            })
      onCreated(sub.id, sub.createdAt)
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : 'Não foi possível criar a subscrição.',
      )
    } finally {
      setBusy(false)
    }
  }

  const canSubmit =
    !busy && (mode === 'concelho' ? chosen != null : point != null)

  return (
    <div className="space-y-3">
      <div>
        <h3 className="text-sm font-semibold text-foreground">Criar alerta</h3>
        <p className="mt-0.5 text-xs text-muted-foreground">
          Receba avisos de novos incêndios, escaladas e risco.
        </p>
      </div>

      <div className="flex gap-1 rounded-xl bg-muted/60 p-1">
        <ModeTab active={mode === 'concelho'} onClick={() => setMode('concelho')}>
          Concelho
        </ModeTab>
        <ModeTab active={mode === 'point'} onClick={() => setMode('point')}>
          Localização
        </ModeTab>
      </div>

      {mode === 'concelho' ? (
        <div className="space-y-2">
          {chosen ? (
            <div className="flex items-center justify-between gap-2 rounded-xl bg-muted/60 px-3 py-2">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-foreground">
                  {chosen.name}
                </p>
                <p className="truncate text-xs text-muted-foreground">
                  {chosen.district}
                </p>
              </div>
              <button
                type="button"
                onClick={() => {
                  setChosen(null)
                  setQuery('')
                }}
                className="text-xs font-medium text-orange-600 hover:underline dark:text-orange-400"
              >
                Mudar
              </button>
            </div>
          ) : (
            <>
              <div className="flex items-center gap-2 rounded-xl bg-muted/60 px-3 py-2">
                <Search className="size-4 shrink-0 text-muted-foreground" aria-hidden />
                <input
                  type="text"
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Procurar concelho…"
                  aria-label="Procurar concelho"
                  className="w-full bg-transparent text-sm text-foreground outline-none placeholder:text-muted-foreground"
                />
              </div>
              {results.length > 0 && (
                <ul className="max-h-40 overflow-y-auto rounded-xl border border-border/60">
                  {results.map((c) => (
                    <li key={c.dico}>
                      <button
                        type="button"
                        onClick={() => setChosen(c)}
                        className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm transition-colors hover:bg-muted/60"
                      >
                        <span className="truncate text-foreground">{c.name}</span>
                        <span className="shrink-0 text-xs text-muted-foreground">
                          {c.district}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}

          <label className="flex items-center justify-between gap-2 text-sm text-foreground">
            <span>Avisar quando o risco for</span>
            <select
              value={riskThreshold}
              onChange={(e) =>
                setRiskThreshold(Number(e.target.value) as 0 | 4 | 5)
              }
              className="rounded-lg border border-border/60 bg-transparent px-2 py-1 text-sm text-foreground outline-none"
            >
              <option value={0}>Nunca</option>
              <option value={4}>Muito elevado</option>
              <option value={5}>Máximo</option>
            </select>
          </label>
        </div>
      ) : (
        <div className="space-y-2">
          <button
            type="button"
            onClick={useMyLocation}
            disabled={locating}
            className="flex w-full items-center justify-center gap-2 rounded-xl bg-muted/60 px-3 py-2 text-sm font-medium text-foreground transition-colors hover:bg-muted disabled:opacity-60"
          >
            {locating ? (
              <Loader2 className="size-4 animate-spin" />
            ) : (
              <MapPin className="size-4" />
            )}
            {point ? 'Atualizar localização' : 'Usar a minha localização'}
          </button>
          {point && (
            <p className="text-xs text-muted-foreground">
              {point.lat.toFixed(3)}, {point.lng.toFixed(3)}
            </p>
          )}
          <label className="block space-y-1 text-sm text-foreground">
            <span className="flex justify-between">
              <span>Raio</span>
              <span className="tabular-nums text-muted-foreground">
                {radiusKm} km
              </span>
            </span>
            <input
              type="range"
              min={1}
              max={50}
              step={1}
              value={radiusKm}
              onChange={(e) => setRadiusKm(Number(e.target.value))}
              className="h-1.5 w-full cursor-pointer appearance-none rounded-full bg-muted accent-orange-500"
            />
          </label>
        </div>
      )}

      {error && <p className="text-xs text-red-600 dark:text-red-400">{error}</p>}

      <button
        type="button"
        onClick={submit}
        disabled={!canSubmit}
        className="flex w-full items-center justify-center gap-2 rounded-xl bg-zinc-900 px-3 py-2 text-sm font-semibold text-white transition-colors hover:bg-zinc-800 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-100"
      >
        {busy && <Loader2 className="size-4 animate-spin" />}
        Ativar alerta
      </button>
    </div>
  )
}

function ModeTab({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        active
          ? 'flex-1 rounded-lg bg-white px-3 py-1.5 text-sm font-medium text-zinc-900 shadow-sm dark:bg-zinc-700 dark:text-zinc-50'
          : 'flex-1 rounded-lg px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:text-foreground'
      }
    >
      {children}
    </button>
  )
}

// ── Manage ───────────────────────────────────────────────────────────────────

function ManageView({
  subscriptionId,
  events,
  onDeleted,
}: {
  subscriptionId: string
  events: AlertEvent[]
  onDeleted: () => void
}) {
  const [busy, setBusy] = useState(false)

  const remove = async () => {
    setBusy(true)
    try {
      await deleteAlertSubscription({ data: subscriptionId })
    } catch {
      // Even if the server call fails, drop the local id so the UI recovers.
    } finally {
      onDeleted()
      setBusy(false)
    }
  }

  const recent = [...events]
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
    .slice(0, 6)

  return (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-2">
        <div>
          <h3 className="text-sm font-semibold text-foreground">
            Alerta ativo
          </h3>
          <p className="mt-0.5 text-xs text-muted-foreground">
            A verificar novos eventos a cada minuto.
          </p>
        </div>
      </div>

      {recent.length > 0 ? (
        <ul className="max-h-56 space-y-2 overflow-y-auto">
          {recent.map((e) => (
            <li key={e.id} className="rounded-xl bg-muted/60 px-3 py-2">
              <div className="flex items-center justify-between gap-2">
                <span className="text-xs font-semibold text-foreground">
                  {alertKindTitle(e.kind)}
                </span>
                <span className="shrink-0 text-[11px] text-muted-foreground">
                  {formatRelative(e.createdAt)}
                </span>
              </div>
              <p className="mt-0.5 text-sm text-foreground">{e.message}</p>
            </li>
          ))}
        </ul>
      ) : (
        <p className="rounded-xl bg-muted/60 px-3 py-4 text-center text-sm text-muted-foreground">
          Sem eventos por agora. Iremos avisá-lo assim que houver novidades.
        </p>
      )}

      <button
        type="button"
        onClick={remove}
        disabled={busy}
        className="flex w-full items-center justify-center gap-2 rounded-xl border border-red-500/30 px-3 py-2 text-sm font-medium text-red-600 transition-colors hover:bg-red-500/10 disabled:opacity-50 dark:text-red-400"
      >
        {busy ? (
          <Loader2 className="size-4 animate-spin" />
        ) : (
          <Trash2 className="size-4" />
        )}
        Remover alerta
      </button>
    </div>
  )
}
