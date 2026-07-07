import { useEffect, useMemo, useRef, useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Dialog as DialogPrimitive } from 'radix-ui'
import {
  Bell,
  BellRing,
  Check,
  ChevronDown,
  Loader2,
  LocateFixed,
  MapPin,
  Plus,
  Trash2,
  TriangleAlert,
} from 'lucide-react'

import {
  createDeviceAlertSubscription,
  deleteDeviceAlertSubscription,
  deviceSubscriptionsQuery,
  webPushPublicKeyQuery,
} from '#/lib/fogos/api.ts'
import type { OwnedAlertSubscription } from '#/lib/fogos/account-api.ts'
import {
  CONCELHOS,
  concelhoByDico,
  searchConcelhos,
} from '#/lib/fogos/concelhos.ts'
import type { ConcelhoEntry } from '#/lib/fogos/concelhos.ts'
import {
  PushPermissionDeniedError,
  disablePush,
  enablePush,
  getStoredDevice,
  iosNeedsInstall,
  supportsPush,
} from '#/lib/push.ts'
import type { StoredDevice } from '#/lib/push.ts'
import { PageHeader } from '#/components/page-header.tsx'

// ── Route ─────────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/alertas')({
  head: () => ({
    meta: [{ title: 'Alertas — FogosPortugal' }],
  }),
  // Resolve the VAPID flag during SSR so the dark/enabled state renders without a
  // client round-trip (mirrors /conta's accounts flag).
  loader: ({ context }) =>
    context.queryClient.ensureQueryData(webPushPublicKeyQuery()).catch(() => null),
  component: Alertas,
})

// ── Shared styling tokens (shared with the rest of the app) ──────────────────

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60'

const PILL_SELECTED =
  'rounded-full bg-orange-500/15 px-3 py-1.5 text-sm font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
const PILL_IDLE =
  'rounded-full bg-muted/60 px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted'

const BTN_PRIMARY =
  'inline-flex items-center justify-center gap-2 rounded-xl bg-zinc-900 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-zinc-800 disabled:opacity-50 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200'
const BTN_GHOST =
  'inline-flex items-center justify-center gap-2 rounded-xl border border-black/5 bg-white/75 px-3 py-1.5 text-sm font-medium text-foreground shadow-sm backdrop-blur-xl transition-colors hover:bg-white/90 disabled:opacity-50 dark:border-white/10 dark:bg-zinc-900/70 dark:hover:bg-zinc-900/90'
const BTN_DANGER =
  'inline-flex items-center justify-center gap-2 rounded-xl px-3 py-1.5 text-sm font-medium text-red-600 transition-colors hover:bg-red-500/10 disabled:opacity-50 dark:text-red-400'

const INPUT_CLASS =
  'w-full rounded-xl border border-black/10 bg-white/70 px-3 py-2 text-sm text-foreground transition-colors placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-orange-500/40 dark:border-white/15 dark:bg-zinc-900/60'
const SELECT_CLASS =
  'w-full appearance-none rounded-xl border border-black/10 bg-white/70 py-2 pl-3 pr-8 text-sm font-medium text-foreground transition-colors hover:bg-white/90 focus:outline-none focus:ring-2 focus:ring-orange-500/40 dark:border-white/15 dark:bg-zinc-900/60 dark:hover:bg-zinc-900/80'

// IPMA fire-risk levels the alert threshold can pin (4 or 5).
const RISK_LABELS: Record<number, string> = {
  4: 'Muito Elevado',
  5: 'Máximo',
}

const RADIUS_OPTIONS = [5, 10, 25, 50] as const

const dateFmt = new Intl.DateTimeFormat('pt-PT', {
  timeZone: 'Europe/Lisbon',
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

function formatDate(iso: string | null): string {
  if (!iso) return '—'
  return dateFmt.format(new Date(iso))
}

/** Map an API error to pt-PT copy, keyed on the GraphQL error code when present. */
function errorToPt(error: unknown): string {
  if (error instanceof PushPermissionDeniedError) return error.message
  const code = (error as { code?: string } | null)?.code
  switch (code) {
    case 'ALERT_SUBSCRIPTION_LIMIT':
      return 'Atingiu o limite de alertas.'
    case 'DEVICE_NOT_FOUND':
      return 'Este dispositivo já não está registado. Reative as notificações.'
    case 'WEB_PUSH_DISABLED':
      return 'As notificações não estão disponíveis de momento.'
    default:
      return 'Ocorreu um erro. Tente novamente.'
  }
}

// ── Page ─────────────────────────────────────────────────────────────────────

function Alertas() {
  const publicKeyQuery = useQuery(webPushPublicKeyQuery())

  // Support detection + stored-device lookup are client-only (they touch
  // navigator / localStorage), so gate them behind a mount flag to keep SSR and
  // the first client paint identical.
  const [mounted, setMounted] = useState(false)
  useEffect(() => setMounted(true), [])

  const supported = mounted && supportsPush()
  const publicKey = publicKeyQuery.data // undefined = loading, null = dark, string = on.

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-3xl px-4 py-6">
        <h1 className="mb-6 text-2xl font-bold text-foreground">Alertas</h1>
        {!mounted || publicKey === undefined ? (
          <CenteredSpinner />
        ) : publicKey === null ? (
          <FeatureDarkCard />
        ) : !supported ? (
          <UnsupportedCard />
        ) : (
          <EnabledGate publicKey={publicKey} />
        )}
      </main>
    </div>
  )
}

function CenteredSpinner() {
  return (
    <div className="flex items-center justify-center py-20">
      <Loader2 className="size-6 animate-spin text-muted-foreground" aria-hidden />
    </div>
  )
}

function FeatureDarkCard() {
  return (
    <div className="mx-auto max-w-md py-10">
      <div className={`${CARD_CLASS} py-10 text-center`}>
        <Bell className="mx-auto mb-3 size-8 text-muted-foreground" aria-hidden />
        <h2 className="text-lg font-semibold text-foreground">Alertas de incêndio</h2>
        <p className="mt-2 text-sm text-muted-foreground">
          Os alertas ainda não estão disponíveis.
        </p>
      </div>
    </div>
  )
}

function UnsupportedCard() {
  return (
    <div className="mx-auto max-w-md py-10">
      <div className={`${CARD_CLASS} py-10 text-center`}>
        <Bell className="mx-auto mb-3 size-8 text-muted-foreground" aria-hidden />
        <h2 className="text-lg font-semibold text-foreground">
          Notificações não suportadas
        </h2>
        <p className="mt-2 text-sm text-muted-foreground">
          O seu navegador não suporta notificações push. Experimente um navegador
          recente no computador ou no telemóvel.
        </p>
      </div>
    </div>
  )
}

// ── Enabled gate (resolves the stored device, then routes) ────────────────────

function EnabledGate({ publicKey }: { publicKey: string }) {
  const queryClient = useQueryClient()

  // `undefined` while resolving, `null` = not enabled, object = enabled. The
  // query itself runs the repair path (clears storage when the browser lost its
  // subscription) so a stale handle never traps the UI in the enabled state.
  const deviceQuery = useQuery({
    queryKey: ['webpush-device'] as const,
    queryFn: () => getStoredDevice(),
    staleTime: Number.POSITIVE_INFINITY,
    gcTime: Number.POSITIVE_INFINITY,
  })

  const setDevice = (device: StoredDevice | null) =>
    queryClient.setQueryData(['webpush-device'], device)

  if (deviceQuery.isLoading) return <CenteredSpinner />

  return deviceQuery.data ? (
    <ManagementView device={deviceQuery.data} onDisabled={() => setDevice(null)} />
  ) : (
    <EnableHero
      publicKey={publicKey}
      onEnabled={(device) => setDevice(device)}
    />
  )
}

// ── Not-enabled: the pitch + the enable button ────────────────────────────────

function EnableHero({
  publicKey,
  onEnabled,
}: {
  publicKey: string
  onEnabled: (device: StoredDevice) => void
}) {
  const mutation = useMutation({
    mutationFn: () => enablePush(publicKey),
    onSuccess: onEnabled,
  })

  const denied = mutation.error instanceof PushPermissionDeniedError
  const showIosHint = iosNeedsInstall()

  return (
    <div className="mx-auto max-w-xl space-y-6 py-6">
      <div className="text-center">
        <div className="mx-auto mb-4 flex size-14 items-center justify-center rounded-2xl bg-orange-500/15 text-orange-600 dark:text-orange-400">
          <BellRing className="size-7" aria-hidden />
        </div>
        <h2 className="text-xl font-bold text-foreground">
          Alertas de incêndio neste dispositivo
        </h2>
        <p className="mx-auto mt-2 max-w-md text-sm text-muted-foreground">
          Receba uma notificação quando um incêndio começa, agrava ou reacende — ou
          quando o risco previsto para o seu concelho é elevado.
        </p>
      </div>

      {showIosHint && (
        <div className="flex items-start gap-2 rounded-xl border border-blue-500/30 bg-blue-500/10 p-3">
          <TriangleAlert
            className="mt-0.5 size-4 shrink-0 text-blue-600 dark:text-blue-400"
            aria-hidden
          />
          <p className="text-sm text-blue-800 dark:text-blue-200">
            No iPhone e iPad, as notificações só funcionam depois de{' '}
            <strong>adicionar a app ao ecrã principal</strong>. Toque em Partilhar e
            escolha «Adicionar ao ecrã principal».
          </p>
        </div>
      )}

      {denied ? (
        <div className="flex items-start gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3">
          <TriangleAlert
            className="mt-0.5 size-4 shrink-0 text-amber-600 dark:text-amber-400"
            aria-hidden
          />
          <p className="text-sm text-amber-800 dark:text-amber-200">
            As notificações estão bloqueadas nas definições do navegador. Ative-as
            para este site e tente novamente.
          </p>
        </div>
      ) : mutation.isError ? (
        <FormError error={mutation.error} />
      ) : null}

      <div className="flex justify-center">
        <button
          type="button"
          className={`${BTN_PRIMARY} px-6 py-2.5`}
          disabled={mutation.isPending}
          onClick={() => mutation.mutate()}
        >
          {mutation.isPending ? (
            <Loader2 className="size-4 animate-spin" aria-hidden />
          ) : (
            <BellRing className="size-4" aria-hidden />
          )}
          Ativar notificações
        </button>
      </div>
    </div>
  )
}

// ── Enabled: status + subscription management ─────────────────────────────────

function ManagementView({
  device,
  onDisabled,
}: {
  device: StoredDevice
  onDisabled: () => void
}) {
  const queryClient = useQueryClient()
  const [creating, setCreating] = useState(false)

  const subsQuery = useQuery(deviceSubscriptionsQuery(device.deviceId))
  const subs = subsQuery.data ?? []

  const invalidate = () =>
    queryClient.invalidateQueries({
      queryKey: ['device-subscriptions', device.deviceId],
    })

  return (
    <div className="space-y-6">
      <StatusRow onDisabled={onDisabled} />

      <section className="space-y-3">
        <div className="flex items-center justify-between gap-3">
          <SectionTitle>Os meus alertas</SectionTitle>
          <button
            type="button"
            className={BTN_PRIMARY}
            onClick={() => setCreating(true)}
          >
            <Plus className="size-4" aria-hidden />
            Criar alerta
          </button>
        </div>

        {subsQuery.isLoading ? (
          <div className={`${CARD_CLASS} py-8 text-center`}>
            <Loader2
              className="mx-auto size-5 animate-spin text-muted-foreground"
              aria-hidden
            />
          </div>
        ) : subs.length === 0 ? (
          <div className={`${CARD_CLASS} py-8 text-center`}>
            <p className="text-sm text-muted-foreground">
              Ainda não configurou nenhum alerta. Crie um para começar a receber
              notificações.
            </p>
          </div>
        ) : (
          <ul className="space-y-2">
            {subs.map((sub) => (
              <SubscriptionRow key={sub.id} sub={sub} onDeleted={invalidate} />
            ))}
          </ul>
        )}
      </section>

      {creating && (
        <CreateDialog
          deviceId={device.deviceId}
          onClose={() => setCreating(false)}
          onCreated={() => {
            setCreating(false)
            invalidate()
          }}
        />
      )}
    </div>
  )
}

function StatusRow({ onDisabled }: { onDisabled: () => void }) {
  const [confirming, setConfirming] = useState(false)
  const mutation = useMutation({
    mutationFn: () => disablePush(),
    onSuccess: () => {
      setConfirming(false)
      onDisabled()
    },
  })

  return (
    <div className={`${CARD_CLASS} flex items-center justify-between gap-3`}>
      <div className="flex items-center gap-3">
        <span className="flex size-9 items-center justify-center rounded-xl bg-green-500/15 text-green-600 dark:text-green-400">
          <BellRing className="size-5" aria-hidden />
        </span>
        <div>
          <p className="text-sm font-medium text-foreground">
            Notificações ativas neste dispositivo
          </p>
          <p className="text-xs text-muted-foreground">
            Este navegador vai receber os alertas que configurar abaixo.
          </p>
        </div>
      </div>

      {confirming ? (
        <div className="flex shrink-0 items-center gap-1">
          <button
            type="button"
            className={BTN_DANGER}
            disabled={mutation.isPending}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending && (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            )}
            Confirmar
          </button>
          <button
            type="button"
            className={BTN_GHOST}
            disabled={mutation.isPending}
            onClick={() => setConfirming(false)}
          >
            Cancelar
          </button>
        </div>
      ) : (
        <button
          type="button"
          className={`${BTN_GHOST} shrink-0`}
          onClick={() => setConfirming(true)}
        >
          Desativar
        </button>
      )}
    </div>
  )
}

function SubscriptionRow({
  sub,
  onDeleted,
}: {
  sub: OwnedAlertSubscription
  onDeleted: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const mutation = useMutation({
    mutationFn: () => deleteDeviceAlertSubscription({ data: sub.id }),
    onSuccess: () => {
      setConfirming(false)
      onDeleted()
    },
  })

  const isConcelho = sub.kind === 'CONCELHO'
  const concelho = sub.dico ? concelhoByDico(sub.dico) : null

  const title = isConcelho
    ? concelho
      ? `Concelho de ${concelho.name}`
      : `Concelho ${sub.dico}`
    : sub.radiusKm != null
      ? `Num raio de ${sub.radiusKm} km da sua localização`
      : 'Junto à sua localização'

  const subtitleParts: string[] = []
  if (isConcelho && concelho) subtitleParts.push(concelho.district)
  if (sub.riskThreshold) {
    subtitleParts.push(
      `risco ≥ ${RISK_LABELS[sub.riskThreshold] ?? sub.riskThreshold}`,
    )
  }
  if (!isConcelho && sub.point) {
    subtitleParts.push(
      `${sub.point.latitude.toFixed(3)}, ${sub.point.longitude.toFixed(3)}`,
    )
  }
  subtitleParts.push(`desde ${formatDate(sub.createdAt)}`)

  return (
    <li className={`${CARD_CLASS} flex items-center justify-between gap-3`}>
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          {isConcelho ? (
            <MapPin className="size-4 shrink-0 text-muted-foreground" aria-hidden />
          ) : (
            <LocateFixed
              className="size-4 shrink-0 text-muted-foreground"
              aria-hidden
            />
          )}
          <span className="truncate font-medium text-foreground">{title}</span>
        </div>
        <p className="mt-1 truncate text-xs text-muted-foreground">
          {subtitleParts.join(' · ')}
        </p>
      </div>

      <div className="flex shrink-0 items-center gap-1">
        {confirming ? (
          <>
            <button
              type="button"
              className={BTN_DANGER}
              disabled={mutation.isPending}
              onClick={() => mutation.mutate()}
            >
              {mutation.isPending && (
                <Loader2 className="size-4 animate-spin" aria-hidden />
              )}
              Confirmar
            </button>
            <button
              type="button"
              className={BTN_GHOST}
              disabled={mutation.isPending}
              onClick={() => setConfirming(false)}
            >
              Cancelar
            </button>
          </>
        ) : (
          <button
            type="button"
            aria-label="Eliminar alerta"
            className={BTN_DANGER}
            onClick={() => setConfirming(true)}
          >
            <Trash2 className="size-4" aria-hidden />
          </button>
        )}
      </div>
    </li>
  )
}

// ── Create dialog ─────────────────────────────────────────────────────────────

interface Coords {
  latitude: number
  longitude: number
}

function CreateDialog({
  deviceId,
  onClose,
  onCreated,
}: {
  deviceId: string
  onClose: () => void
  onCreated: () => void
}) {
  const [kind, setKind] = useState<'CONCELHO' | 'POINT'>('CONCELHO')
  const [dico, setDico] = useState<string | null>(null)
  const [riskThreshold, setRiskThreshold] = useState<number | null>(null)
  const [coords, setCoords] = useState<Coords | null>(null)
  const [radiusKm, setRadiusKm] = useState<number>(10)
  const [geoState, setGeoState] = useState<'idle' | 'loading' | 'error'>('idle')

  const mutation = useMutation({
    mutationFn: () =>
      createDeviceAlertSubscription({
        data:
          kind === 'CONCELHO'
            ? { kind: 'CONCELHO', dico, riskThreshold, deviceId }
            : {
                kind: 'POINT',
                latitude: coords?.latitude ?? null,
                longitude: coords?.longitude ?? null,
                radiusKm,
                deviceId,
              },
      }),
    onSuccess: onCreated,
  })

  const valid = kind === 'CONCELHO' ? dico != null : coords != null

  const requestLocation = () => {
    if (typeof navigator === 'undefined' || !navigator.geolocation) {
      setGeoState('error')
      return
    }
    setGeoState('loading')
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setCoords({
          latitude: pos.coords.latitude,
          longitude: pos.coords.longitude,
        })
        setGeoState('idle')
      },
      () => setGeoState('error'),
      { enableHighAccuracy: true, timeout: 10_000 },
    )
  }

  return (
    <Modal
      title="Criar alerta"
      description="Escolha um concelho ou um ponto para receber alertas de incêndio."
      onClose={onClose}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault()
          if (valid && !mutation.isPending) mutation.mutate()
        }}
        className="space-y-4"
      >
        {/* Segmented control */}
        <div className="flex gap-2">
          <button
            type="button"
            aria-pressed={kind === 'CONCELHO'}
            onClick={() => setKind('CONCELHO')}
            className={kind === 'CONCELHO' ? PILL_SELECTED : PILL_IDLE}
          >
            Concelho
          </button>
          <button
            type="button"
            aria-pressed={kind === 'POINT'}
            onClick={() => setKind('POINT')}
            className={kind === 'POINT' ? PILL_SELECTED : PILL_IDLE}
          >
            Local
          </button>
        </div>

        {kind === 'CONCELHO' ? (
          <>
            <div className="space-y-1.5">
              <label id="concelho-label" className="text-sm font-medium text-foreground">
                Concelho
              </label>
              <ConcelhoPicker value={dico} onChange={setDico} labelId="concelho-label" />
            </div>
            <div className="space-y-1.5">
              <label htmlFor="risk" className="text-sm font-medium text-foreground">
                Risco mínimo
              </label>
              <div className="relative">
                <select
                  id="risk"
                  value={riskThreshold ?? ''}
                  onChange={(e) =>
                    setRiskThreshold(e.target.value ? Number(e.target.value) : null)
                  }
                  className={SELECT_CLASS}
                >
                  <option value="">Nenhum</option>
                  <option value="4">Muito Elevado</option>
                  <option value="5">Máximo</option>
                </select>
                <ChevronDown
                  aria-hidden
                  className="pointer-events-none absolute right-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                />
              </div>
              <p className="text-xs text-muted-foreground">
                Receba também um aviso quando o risco previsto atingir este nível.
              </p>
            </div>
          </>
        ) : (
          <>
            <div className="space-y-1.5">
              <span className="text-sm font-medium text-foreground">Localização</span>
              <button
                type="button"
                className={`${BTN_GHOST} w-full justify-center`}
                disabled={geoState === 'loading'}
                onClick={requestLocation}
              >
                {geoState === 'loading' ? (
                  <Loader2 className="size-4 animate-spin" aria-hidden />
                ) : (
                  <LocateFixed className="size-4" aria-hidden />
                )}
                {coords ? 'Atualizar localização' : 'Usar a minha localização'}
              </button>
              {coords && (
                <p className="text-xs text-muted-foreground">
                  {coords.latitude.toFixed(3)}, {coords.longitude.toFixed(3)}
                </p>
              )}
              {geoState === 'error' && (
                <p className="text-xs text-red-600 dark:text-red-400">
                  Não foi possível obter a sua localização. Verifique as permissões.
                </p>
              )}
            </div>
            <div className="space-y-1.5">
              <label htmlFor="radius" className="text-sm font-medium text-foreground">
                Raio
              </label>
              <div className="relative">
                <select
                  id="radius"
                  value={radiusKm}
                  onChange={(e) => setRadiusKm(Number(e.target.value))}
                  className={SELECT_CLASS}
                >
                  {RADIUS_OPTIONS.map((km) => (
                    <option key={km} value={km}>
                      {km} km
                    </option>
                  ))}
                </select>
                <ChevronDown
                  aria-hidden
                  className="pointer-events-none absolute right-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                />
              </div>
            </div>
          </>
        )}

        {mutation.isError && <FormError error={mutation.error} />}

        <div className="flex justify-end gap-2">
          <button type="button" className={BTN_GHOST} onClick={onClose}>
            Cancelar
          </button>
          <button
            type="submit"
            className={BTN_PRIMARY}
            disabled={!valid || mutation.isPending}
          >
            {mutation.isPending && (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            )}
            Criar alerta
          </button>
        </div>
      </form>
    </Modal>
  )
}

// ── Concelho picker (searchable dialog, grouped by district) ──────────────────

function ConcelhoPicker({
  value,
  onChange,
  labelId,
}: {
  value: string | null
  onChange: (dico: string | null) => void
  labelId?: string
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  const selected = value ? concelhoByDico(value) : null

  const grouped = useMemo(() => {
    const matches = query.trim() ? searchConcelhos(query, 40) : CONCELHOS.slice(0, 40)
    const byDistrict = new Map<string, ConcelhoEntry[]>()
    for (const c of matches) {
      const list = byDistrict.get(c.district) ?? []
      list.push(c)
      byDistrict.set(c.district, list)
    }
    return [...byDistrict.entries()].sort((a, b) =>
      a[0].localeCompare(b[0], 'pt-PT'),
    )
  }, [query])

  return (
    <DialogPrimitive.Root
      open={open}
      onOpenChange={(next) => {
        setOpen(next)
        if (next) setQuery('')
      }}
    >
      <DialogPrimitive.Trigger asChild>
        <button
          type="button"
          aria-labelledby={labelId}
          className={`${SELECT_CLASS} flex items-center justify-between text-left`}
        >
          <span className={selected ? 'text-foreground' : 'text-muted-foreground'}>
            {selected ? `${selected.name} · ${selected.district}` : 'Escolher concelho'}
          </span>
          <ChevronDown className="size-4 shrink-0 text-muted-foreground" aria-hidden />
        </button>
      </DialogPrimitive.Trigger>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 backdrop-blur-sm data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0" />
        <DialogPrimitive.Content
          onOpenAutoFocus={(e) => {
            e.preventDefault()
            inputRef.current?.focus()
          }}
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[70vh] w-[calc(100vw-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-2xl border border-black/10 bg-white/90 shadow-2xl backdrop-blur-xl data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95 dark:border-white/10 dark:bg-zinc-900/90"
        >
          <DialogPrimitive.Title className="sr-only">
            Escolher concelho
          </DialogPrimitive.Title>
          <DialogPrimitive.Description className="sr-only">
            Procure e selecione o concelho para o alerta.
          </DialogPrimitive.Description>
          <div className="border-b border-black/5 p-3 dark:border-white/10">
            <input
              ref={inputRef}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Procurar concelho…"
              className={INPUT_CLASS}
            />
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto p-2">
            {grouped.length === 0 ? (
              <p className="px-2 py-6 text-center text-sm text-muted-foreground">
                Sem resultados.
              </p>
            ) : (
              grouped.map(([district, items]) => (
                <div key={district} className="mb-2">
                  <p className="px-2 py-1 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                    {district}
                  </p>
                  {items.map((c) => (
                    <button
                      key={c.dico}
                      type="button"
                      onClick={() => {
                        onChange(c.dico)
                        setOpen(false)
                      }}
                      className={`flex w-full items-center justify-between rounded-lg px-2 py-1.5 text-left text-sm transition-colors hover:bg-black/5 dark:hover:bg-white/10 ${
                        c.dico === value
                          ? 'text-orange-600 dark:text-orange-400'
                          : 'text-foreground'
                      }`}
                    >
                      {c.name}
                      {c.dico === value && <Check className="size-4" aria-hidden />}
                    </button>
                  ))}
                </div>
              ))
            )}
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}

// ── Shared primitives ─────────────────────────────────────────────────────────

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
      {children}
    </h2>
  )
}

function Modal({
  title,
  description,
  onClose,
  children,
}: {
  title: string
  description: string
  onClose: () => void
  children: React.ReactNode
}) {
  return (
    <DialogPrimitive.Root open onOpenChange={(open) => !open && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 backdrop-blur-sm data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0" />
        <DialogPrimitive.Content className="fixed left-1/2 top-1/2 z-50 w-[calc(100vw-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-black/10 bg-white/90 p-5 shadow-2xl backdrop-blur-xl data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95 dark:border-white/10 dark:bg-zinc-900/90">
          <div className="mb-4">
            <DialogPrimitive.Title className="text-lg font-semibold text-foreground">
              {title}
            </DialogPrimitive.Title>
          </div>
          <DialogPrimitive.Description className="sr-only">
            {description}
          </DialogPrimitive.Description>
          {children}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}

function FormError({ error }: { error: unknown }) {
  return (
    <div className="flex items-start gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-3">
      <TriangleAlert
        className="mt-0.5 size-4 shrink-0 text-red-600 dark:text-red-400"
        aria-hidden
      />
      <p className="text-sm text-red-700 dark:text-red-300">{errorToPt(error)}</p>
    </div>
  )
}
