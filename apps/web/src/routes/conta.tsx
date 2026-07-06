import { useMemo, useRef, useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import {
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { SignIn, UserButton, useAuth } from '@clerk/tanstack-react-start'
import { Dialog as DialogPrimitive } from 'radix-ui'
import {
  Check,
  ChevronDown,
  Copy,
  KeyRound,
  Loader2,
  MapPin,
  Plus,
  Trash2,
  TriangleAlert,
  Webhook as WebhookIcon,
  X,
} from 'lucide-react'

import {
  createAlertSubscription,
  createApiKey,
  deleteAlertSubscription,
  meQuery,
  revokeApiKey,
  updateAlertSubscription,
} from '#/lib/fogos/account-api.ts'
import type {
  AlertSubscriptionInput,
  ApiKeyInfo,
  CreatedApiKey,
  Me,
  OwnedAlertSubscription,
  Webhook,
} from '#/lib/fogos/account-api.ts'
import {
  CONCELHOS,
  concelhoByDico,
  searchConcelhos,
} from '#/lib/fogos/concelhos.ts'
import type { ConcelhoEntry } from '#/lib/fogos/concelhos.ts'
import { PageHeader } from '#/components/page-header.tsx'
import { Skeleton } from '#/components/ui/skeleton.tsx'

// Caps mirror the API (AuthOptions.MaxApiKeysPerUser / AlertOptions.MaxSubscriptionsPerUser).
const MAX_API_KEYS = 3
const MAX_SUBSCRIPTIONS = 10

// ── Search params ────────────────────────────────────────────────────────────

type ContaTab = 'chaves' | 'alertas'

interface ContaSearch {
  tab: ContaTab
}

export const Route = createFileRoute('/conta')({
  head: () => ({
    meta: [{ title: 'A minha conta — FogosPortugal' }],
  }),
  validateSearch: (search: Record<string, unknown>): ContaSearch => ({
    tab: search.tab === 'alertas' ? 'alertas' : 'chaves',
  }),
  component: Conta,
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

function SectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
      {children}
    </h2>
  )
}

// ── Date formatting (Lisbon) ─────────────────────────────────────────────────

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

// ── Page ─────────────────────────────────────────────────────────────────────

function Conta() {
  // Clerk 6 has no <SignedIn>/<SignedOut> components — gate on the hook. Until
  // Clerk loads (or forever, if the keys are unset) `isLoaded` is false.
  const { isLoaded, isSignedIn } = useAuth()

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-3xl px-4 py-6">
        {!isLoaded ? (
          <div className="flex items-center justify-center py-20">
            <Loader2
              className="size-6 animate-spin text-muted-foreground"
              aria-hidden
            />
          </div>
        ) : isSignedIn ? (
          <SignedInView />
        ) : (
          <SignedOutView />
        )}
      </main>
    </div>
  )
}

function SignedOutView() {
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-6 py-10">
      <div className="text-center">
        <h1 className="text-2xl font-bold text-foreground">A minha conta</h1>
        <p className="mt-2 text-sm text-muted-foreground">
          Inicie sessão para gerir as suas chaves de API e os seus alertas de
          incêndio.
        </p>
      </div>
      {/* Clerk's own sign-in widget (already a self-contained card); hash routing
          keeps it embedded on /conta without extra catch-all routes. */}
      <SignIn routing="hash" />
    </div>
  )
}

function SignedInView() {
  const search = Route.useSearch()
  const navigate = Route.useNavigate()
  const { isSignedIn } = useAuth()

  const meResult = useQuery({ ...meQuery(), enabled: !!isSignedIn })
  const me = meResult.data ?? null

  const setTab = (tab: ContaTab) =>
    navigate({ search: { tab }, replace: true })

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-2xl font-bold text-foreground">A minha conta</h1>
        <UserButton />
      </div>

      {/* Tabs */}
      <div className="flex gap-2" role="tablist" aria-label="Secções da conta">
        <button
          type="button"
          role="tab"
          aria-selected={search.tab === 'chaves'}
          onClick={() => setTab('chaves')}
          className={search.tab === 'chaves' ? PILL_SELECTED : PILL_IDLE}
        >
          Chaves de API
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={search.tab === 'alertas'}
          onClick={() => setTab('alertas')}
          className={search.tab === 'alertas' ? PILL_SELECTED : PILL_IDLE}
        >
          Alertas
        </button>
      </div>

      {meResult.isLoading ? (
        <LoadingCard />
      ) : meResult.isError || !me ? (
        <ErrorCard onRetry={() => meResult.refetch()} />
      ) : search.tab === 'chaves' ? (
        <KeysTab me={me} />
      ) : (
        <AlertsTab me={me} />
      )}
    </div>
  )
}

function LoadingCard() {
  return (
    <div className="space-y-2">
      {Array.from({ length: 3 }).map((_, i) => (
        <Skeleton key={i} className="h-16 w-full" />
      ))}
    </div>
  )
}

function ErrorCard({ onRetry }: { onRetry: () => void }) {
  return (
    <div className={`${CARD_CLASS} flex flex-col items-center gap-3 py-10 text-center`}>
      <p className="text-sm text-muted-foreground">
        Não foi possível carregar os dados da conta.
      </p>
      <button type="button" onClick={onRetry} className={BTN_GHOST}>
        Tentar novamente
      </button>
    </div>
  )
}

// ── Chaves de API tab ────────────────────────────────────────────────────────

function KeysTab({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [creating, setCreating] = useState(false)
  const [created, setCreated] = useState<CreatedApiKey | null>(null)

  const activeKeys = me.apiKeys.filter((k) => k.revokedAt == null)
  const atCap = activeKeys.length >= MAX_API_KEYS

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ['me'] })

  return (
    <div className="space-y-6">
      <section className="space-y-3">
        <div className="flex items-center justify-between gap-3">
          <SectionTitle>Chaves de API</SectionTitle>
          <button
            type="button"
            className={BTN_PRIMARY}
            disabled={atCap}
            onClick={() => setCreating(true)}
          >
            <Plus className="size-4" aria-hidden />
            Criar chave
          </button>
        </div>

        <p className="text-xs text-muted-foreground">
          Cada chave autentica pedidos à API com o limite de utilização de
          utilizador registado. Máximo de {MAX_API_KEYS} chaves ativas
          {atCap ? ' — limite atingido.' : '.'}
        </p>

        {me.apiKeys.length === 0 ? (
          <div className={`${CARD_CLASS} py-8 text-center`}>
            <p className="text-sm text-muted-foreground">
              Ainda não criou nenhuma chave de API.
            </p>
          </div>
        ) : (
          <ul className="space-y-2">
            {me.apiKeys.map((key) => (
              <ApiKeyRow key={key.id} apiKey={key} onRevoked={invalidate} />
            ))}
          </ul>
        )}
      </section>

      <WebhooksSection webhooks={me.webhooks} />

      {creating && (
        <CreateKeyDialog
          onClose={() => setCreating(false)}
          onCreated={(result) => {
            setCreating(false)
            setCreated(result)
            invalidate()
          }}
        />
      )}

      {created && (
        <ShowKeyDialog created={created} onClose={() => setCreated(null)} />
      )}
    </div>
  )
}

function ApiKeyRow({
  apiKey,
  onRevoked,
}: {
  apiKey: ApiKeyInfo
  onRevoked: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const revoked = apiKey.revokedAt != null

  const mutation = useMutation({
    mutationFn: () => revokeApiKey({ data: apiKey.id }),
    onSuccess: () => {
      setConfirming(false)
      onRevoked()
    },
  })

  return (
    <li className={`${CARD_CLASS} flex items-center justify-between gap-3`}>
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <KeyRound
            className="size-4 shrink-0 text-muted-foreground"
            aria-hidden
          />
          <span className="truncate font-medium text-foreground">
            {apiKey.name || 'Chave sem nome'}
          </span>
          {revoked && (
            <span className="shrink-0 rounded-full bg-red-500/15 px-2 py-0.5 text-[11px] font-medium text-red-700 dark:text-red-300">
              Revogada
            </span>
          )}
        </div>
        <p className="mt-1 truncate font-mono text-xs text-muted-foreground">
          {apiKey.keyPrefix ? `${apiKey.keyPrefix}…` : '—'}
          <span className="ml-2 font-sans">Criada em {formatDate(apiKey.createdAt)}</span>
        </p>
      </div>

      {!revoked &&
        (confirming ? (
          <div className="flex shrink-0 items-center gap-1">
            <button
              type="button"
              className={BTN_DANGER}
              disabled={mutation.isPending}
              onClick={() => mutation.mutate()}
            >
              {mutation.isPending ? (
                <Loader2 className="size-4 animate-spin" aria-hidden />
              ) : null}
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
            className={`${BTN_DANGER} shrink-0`}
            onClick={() => setConfirming(true)}
          >
            <Trash2 className="size-4" aria-hidden />
            Revogar
          </button>
        ))}
    </li>
  )
}

function CreateKeyDialog({
  onClose,
  onCreated,
}: {
  onClose: () => void
  onCreated: (result: CreatedApiKey) => void
}) {
  const [name, setName] = useState('')

  const mutation = useMutation({
    mutationFn: () => createApiKey({ data: name.trim() }),
    onSuccess: onCreated,
  })

  const canSubmit = name.trim().length > 0 && !mutation.isPending

  return (
    <Modal title="Criar chave de API" onClose={onClose}>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          if (canSubmit) mutation.mutate()
        }}
        className="space-y-4"
      >
        <div className="space-y-1.5">
          <label htmlFor="key-name" className="text-sm font-medium text-foreground">
            Nome da chave
          </label>
          <input
            id="key-name"
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Ex.: Painel meteorológico"
            className={INPUT_CLASS}
          />
          <p className="text-xs text-muted-foreground">
            Um nome ajuda-o a reconhecer para que serve cada chave.
          </p>
        </div>

        {mutation.isError && <FormError error={mutation.error} />}

        <div className="flex justify-end gap-2">
          <button type="button" className={BTN_GHOST} onClick={onClose}>
            Cancelar
          </button>
          <button type="submit" className={BTN_PRIMARY} disabled={!canSubmit}>
            {mutation.isPending && (
              <Loader2 className="size-4 animate-spin" aria-hidden />
            )}
            Criar chave
          </button>
        </div>
      </form>
    </Modal>
  )
}

function ShowKeyDialog({
  created,
  onClose,
}: {
  created: CreatedApiKey
  onClose: () => void
}) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(created.plaintextKey)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard blocked — the key stays visible for manual copy.
    }
  }

  return (
    <Modal title="Chave criada" onClose={onClose}>
      <div className="space-y-4">
        <div className="flex items-start gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 p-3">
          <TriangleAlert
            className="mt-0.5 size-4 shrink-0 text-amber-600 dark:text-amber-400"
            aria-hidden
          />
          <p className="text-sm text-amber-800 dark:text-amber-200">
            Copie esta chave agora. Por segurança, <strong>não voltará a ser
            mostrada</strong>.
          </p>
        </div>

        <div className="flex items-center gap-2 rounded-xl border border-black/10 bg-white/70 p-3 dark:border-white/15 dark:bg-zinc-900/60">
          <code className="min-w-0 flex-1 break-all font-mono text-sm text-foreground">
            {created.plaintextKey}
          </code>
          <button
            type="button"
            onClick={copy}
            aria-label="Copiar chave"
            className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
          >
            {copied ? (
              <Check className="size-4 text-green-600 dark:text-green-400" aria-hidden />
            ) : (
              <Copy className="size-4" aria-hidden />
            )}
          </button>
        </div>

        <div className="flex justify-end">
          <button type="button" className={BTN_PRIMARY} onClick={onClose}>
            Concluído
          </button>
        </div>
      </div>
    </Modal>
  )
}

function WebhooksSection({ webhooks }: { webhooks: Webhook[] }) {
  return (
    <section className="space-y-3">
      <SectionTitle>Webhooks</SectionTitle>
      <p className="text-xs text-muted-foreground">
        Endpoints associados às suas chaves. Geridos através da API.
      </p>
      {webhooks.length === 0 ? (
        <div className={`${CARD_CLASS} py-8 text-center`}>
          <p className="text-sm text-muted-foreground">
            Nenhum webhook registado.
          </p>
        </div>
      ) : (
        <ul className="space-y-2">
          {webhooks.map((hook) => (
            <li
              key={hook.id}
              className={`${CARD_CLASS} flex items-center justify-between gap-3`}
            >
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <WebhookIcon
                    className="size-4 shrink-0 text-muted-foreground"
                    aria-hidden
                  />
                  <span className="truncate font-mono text-sm text-foreground">
                    {hook.url}
                  </span>
                </div>
                <p className="mt-1 truncate text-xs text-muted-foreground">
                  {hook.events.join(', ') || 'Sem eventos'}
                </p>
              </div>
              <span
                className={`shrink-0 rounded-full px-2 py-0.5 text-[11px] font-medium ${
                  hook.active
                    ? 'bg-green-500/15 text-green-700 dark:text-green-300'
                    : 'bg-muted text-muted-foreground'
                }`}
              >
                {hook.active ? 'Ativo' : 'Inativo'}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

// ── Alertas tab ──────────────────────────────────────────────────────────────

const RISK_LABELS: Record<number, string> = {
  4: 'Risco alto',
  5: 'Risco máximo',
}

function AlertsTab({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<OwnedAlertSubscription | null>(null)
  const [creating, setCreating] = useState(false)

  const atCap = me.alertSubscriptions.length >= MAX_SUBSCRIPTIONS

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ['me'] })

  return (
    <div className="space-y-6">
      <section className="space-y-3">
        <div className="flex items-center justify-between gap-3">
          <SectionTitle>Os meus alertas</SectionTitle>
          <button
            type="button"
            className={BTN_PRIMARY}
            disabled={atCap}
            onClick={() => setCreating(true)}
          >
            <Plus className="size-4" aria-hidden />
            Criar alerta
          </button>
        </div>

        <p className="text-xs text-muted-foreground">
          Receba avisos de incêndios num concelho ou junto a um ponto. Máximo de{' '}
          {MAX_SUBSCRIPTIONS} alertas
          {atCap ? ' — limite atingido.' : '.'}
        </p>

        {me.alertSubscriptions.length === 0 ? (
          <div className={`${CARD_CLASS} py-8 text-center`}>
            <p className="text-sm text-muted-foreground">
              Ainda não configurou nenhum alerta.
            </p>
          </div>
        ) : (
          <ul className="space-y-2">
            {me.alertSubscriptions.map((sub) => (
              <AlertRow
                key={sub.id}
                sub={sub}
                onEdit={() => setEditing(sub)}
                onDeleted={invalidate}
              />
            ))}
          </ul>
        )}
      </section>

      {creating && (
        <AlertDialog
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            invalidate()
          }}
        />
      )}

      {editing && (
        <AlertDialog
          existing={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            invalidate()
          }}
        />
      )}
    </div>
  )
}

function AlertRow({
  sub,
  onEdit,
  onDeleted,
}: {
  sub: OwnedAlertSubscription
  onEdit: () => void
  onDeleted: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const mutation = useMutation({
    mutationFn: () => deleteAlertSubscription({ data: sub.id }),
    onSuccess: () => {
      setConfirming(false)
      onDeleted()
    },
  })

  const concelho = sub.dico ? concelhoByDico(sub.dico) : null

  return (
    <li className={`${CARD_CLASS} flex items-center justify-between gap-3`}>
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <MapPin
            className="size-4 shrink-0 text-muted-foreground"
            aria-hidden
          />
          <span className="truncate font-medium text-foreground">
            {sub.kind === 'CONCELHO'
              ? concelho
                ? `${concelho.name}`
                : `Concelho ${sub.dico}`
              : 'Ponto no mapa'}
          </span>
        </div>
        <p className="mt-1 truncate text-xs text-muted-foreground">
          {sub.kind === 'CONCELHO' ? (
            <>
              {concelho ? `${concelho.district} · ` : ''}
              {sub.riskThreshold
                ? RISK_LABELS[sub.riskThreshold] ?? `Risco ≥ ${sub.riskThreshold}`
                : 'Qualquer incêndio'}
            </>
          ) : (
            <>
              {sub.point
                ? `${sub.point.latitude.toFixed(4)}, ${sub.point.longitude.toFixed(4)}`
                : '—'}
              {sub.radiusKm ? ` · ${sub.radiusKm} km` : ''}
            </>
          )}
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
          <>
            <button type="button" className={BTN_GHOST} onClick={onEdit}>
              Editar
            </button>
            <button
              type="button"
              aria-label="Eliminar alerta"
              className={BTN_DANGER}
              onClick={() => setConfirming(true)}
            >
              <Trash2 className="size-4" aria-hidden />
            </button>
          </>
        )}
      </div>
    </li>
  )
}

function AlertDialog({
  existing,
  onClose,
  onSaved,
}: {
  existing?: OwnedAlertSubscription
  onClose: () => void
  onSaved: () => void
}) {
  const [kind, setKind] = useState<'CONCELHO' | 'POINT'>(
    existing?.kind ?? 'CONCELHO',
  )
  const [dico, setDico] = useState<string | null>(existing?.dico ?? null)
  const [riskThreshold, setRiskThreshold] = useState<number | null>(
    existing?.riskThreshold ?? null,
  )
  const [lat, setLat] = useState(
    existing?.point ? String(existing.point.latitude) : '',
  )
  const [lng, setLng] = useState(
    existing?.point ? String(existing.point.longitude) : '',
  )
  const [radiusKm, setRadiusKm] = useState(
    existing?.radiusKm ? String(existing.radiusKm) : '10',
  )

  const buildInput = (): AlertSubscriptionInput =>
    kind === 'CONCELHO'
      ? { kind, dico, riskThreshold }
      : {
          kind,
          latitude: lat ? Number(lat) : null,
          longitude: lng ? Number(lng) : null,
          radiusKm: radiusKm ? Number(radiusKm) : null,
        }

  const mutation = useMutation({
    mutationFn: () =>
      existing
        ? updateAlertSubscription({ data: { id: existing.id, input: buildInput() } })
        : createAlertSubscription({ data: buildInput() }),
    onSuccess: onSaved,
  })

  const valid =
    kind === 'CONCELHO'
      ? dico != null
      : lat !== '' && lng !== '' && radiusKm !== ''

  return (
    <Modal
      title={existing ? 'Editar alerta' : 'Criar alerta'}
      onClose={onClose}
    >
      <form
        onSubmit={(e) => {
          e.preventDefault()
          if (valid && !mutation.isPending) mutation.mutate()
        }}
        className="space-y-4"
      >
        {/* Kind toggle */}
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
            Ponto
          </button>
        </div>

        {kind === 'CONCELHO' ? (
          <>
            <div className="space-y-1.5">
              <label className="text-sm font-medium text-foreground">
                Concelho
              </label>
              <ConcelhoPicker value={dico} onChange={setDico} />
            </div>
            <div className="space-y-1.5">
              <label
                htmlFor="risk"
                className="text-sm font-medium text-foreground"
              >
                Nível de risco
              </label>
              <div className="relative">
                <select
                  id="risk"
                  value={riskThreshold ?? ''}
                  onChange={(e) =>
                    setRiskThreshold(
                      e.target.value ? Number(e.target.value) : null,
                    )
                  }
                  className={SELECT_CLASS}
                >
                  <option value="">Qualquer incêndio</option>
                  <option value="4">Apenas risco alto (4)</option>
                  <option value="5">Apenas risco máximo (5)</option>
                </select>
                <ChevronDown
                  aria-hidden
                  className="pointer-events-none absolute right-2 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                />
              </div>
            </div>
          </>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <label
                  htmlFor="lat"
                  className="text-sm font-medium text-foreground"
                >
                  Latitude
                </label>
                <input
                  id="lat"
                  inputMode="decimal"
                  value={lat}
                  onChange={(e) => setLat(e.target.value)}
                  placeholder="39.5"
                  className={INPUT_CLASS}
                />
              </div>
              <div className="space-y-1.5">
                <label
                  htmlFor="lng"
                  className="text-sm font-medium text-foreground"
                >
                  Longitude
                </label>
                <input
                  id="lng"
                  inputMode="decimal"
                  value={lng}
                  onChange={(e) => setLng(e.target.value)}
                  placeholder="-8.0"
                  className={INPUT_CLASS}
                />
              </div>
            </div>
            <div className="space-y-1.5">
              <label
                htmlFor="radius"
                className="text-sm font-medium text-foreground"
              >
                Raio (km)
              </label>
              <input
                id="radius"
                inputMode="decimal"
                value={radiusKm}
                onChange={(e) => setRadiusKm(e.target.value)}
                placeholder="10"
                className={INPUT_CLASS}
              />
              <p className="text-xs text-muted-foreground">
                Entre 1 e 50 km, dentro de Portugal continental.
              </p>
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
            {existing ? 'Guardar' : 'Criar alerta'}
          </button>
        </div>
      </form>
    </Modal>
  )
}

// Searchable concelho picker (Popover), grouped by district. Uses the static
// concelho set — no GraphQL locations query exists (see concelhos.ts).
function ConcelhoPicker({
  value,
  onChange,
}: {
  value: string | null
  onChange: (dico: string | null) => void
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const inputRef = useRef<HTMLInputElement>(null)

  const selected = value ? concelhoByDico(value) : null

  const grouped = useMemo(() => {
    const matches = query.trim()
      ? searchConcelhos(query, 40)
      : CONCELHOS.slice(0, 40)
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
                        c.dico === value ? 'text-orange-600 dark:text-orange-400' : 'text-foreground'
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

// ── Shared modal + error primitives ──────────────────────────────────────────

function Modal({
  title,
  onClose,
  children,
}: {
  title: string
  onClose: () => void
  children: React.ReactNode
}) {
  return (
    <DialogPrimitive.Root open onOpenChange={(open) => !open && onClose()}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 backdrop-blur-sm data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0" />
        <DialogPrimitive.Content className="fixed left-1/2 top-1/2 z-50 w-[calc(100vw-2rem)] max-w-md -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-black/10 bg-white/90 p-5 shadow-2xl backdrop-blur-xl data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95 data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95 dark:border-white/10 dark:bg-zinc-900/90">
          <div className="mb-4 flex items-start justify-between gap-2">
            <DialogPrimitive.Title className="text-lg font-semibold text-foreground">
              {title}
            </DialogPrimitive.Title>
            <DialogPrimitive.Close
              aria-label="Fechar"
              className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
            >
              <X className="size-4" aria-hidden />
            </DialogPrimitive.Close>
          </div>
          {children}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}

function FormError({ error }: { error: unknown }) {
  const message =
    error instanceof Error ? error.message : 'Ocorreu um erro. Tente novamente.'
  return (
    <div className="flex items-start gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-3">
      <TriangleAlert
        className="mt-0.5 size-4 shrink-0 text-red-600 dark:text-red-400"
        aria-hidden
      />
      <p className="text-sm text-red-700 dark:text-red-300">{message}</p>
    </div>
  )
}
