import { createFileRoute } from '@tanstack/react-router'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { ExternalLink } from 'lucide-react'

import { warningsQuery } from '#/lib/fogos/api.ts'
import { formatRelative } from '#/lib/fogos/format.ts'
import type { Warning, WarningKind } from '#/lib/fogos/types.ts'
import { PageHeader } from '#/components/page-header.tsx'
import { Skeleton } from '#/components/ui/skeleton.tsx'

export const Route = createFileRoute('/avisos')({
  head: () => ({
    meta: [{ title: 'Avisos — FogosPortugal' }],
  }),
  component: Avisos,
  loader: ({ context }) =>
    context.queryClient.ensureQueryData(warningsQuery(null)).catch(() => null),
})

// ── Styling tokens (shared with the rest of the app) ─────────────────────────

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60'

const PILL_SELECTED =
  'rounded-full bg-orange-500/15 px-2.5 py-1 text-xs font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
const PILL_IDLE =
  'rounded-full bg-muted/60 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted'

// ── Kind presentation ────────────────────────────────────────────────────────

const KIND_LABEL: Record<string, string> = {
  MANUAL: 'Manual',
  AGIF: 'AGIF',
  SITE: 'Oficial',
}

/** Muted per-kind badge; warnings are serious content, so no loud reds. */
const KIND_BADGE: Record<string, string> = {
  MANUAL: 'bg-amber-500/12 text-amber-700 ring-amber-500/25 dark:text-amber-300',
  AGIF: 'bg-sky-500/12 text-sky-700 ring-sky-500/25 dark:text-sky-300',
  SITE: 'bg-violet-500/12 text-violet-700 ring-violet-500/25 dark:text-violet-300',
}

function kindLabel(kind: WarningKind): string {
  return KIND_LABEL[kind] ?? kind
}

function kindBadge(kind: WarningKind): string {
  return KIND_BADGE[kind] ?? 'bg-muted/70 text-muted-foreground ring-black/5 dark:ring-white/10'
}

// The order kinds appear in the filter row when present in the data.
const KIND_ORDER: WarningKind[] = ['MANUAL', 'AGIF', 'SITE']

// ── Page ─────────────────────────────────────────────────────────────────────

function Avisos() {
  const query = useQuery(warningsQuery(null))
  const warnings = query.data ?? []
  const [active, setActive] = useState<WarningKind | null>(null)

  const present = KIND_ORDER.filter((k) => warnings.some((w) => w.kind === k))
  const visible = active ? warnings.filter((w) => w.kind === active) : warnings

  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-3xl px-4 py-6">
        <h1 className="mb-6 text-2xl font-bold text-foreground">Avisos</h1>

        {/* Filter chips — only when more than one kind is present. */}
        {present.length > 1 && (
          <div className="mb-6 flex flex-wrap gap-1.5">
            <button
              type="button"
              aria-pressed={active === null}
              onClick={() => setActive(null)}
              className={active === null ? PILL_SELECTED : PILL_IDLE}
            >
              Todos
            </button>
            {present.map((kind) => (
              <button
                key={kind}
                type="button"
                aria-pressed={active === kind}
                onClick={() => setActive(kind)}
                className={active === kind ? PILL_SELECTED : PILL_IDLE}
              >
                {kindLabel(kind)}
              </button>
            ))}
          </div>
        )}

        {query.isLoading ? (
          <ListSkeleton />
        ) : visible.length === 0 ? (
          <div className="flex flex-col items-center gap-2 py-16 text-center">
            <p className="text-sm text-muted-foreground">
              Sem avisos de momento. Boa notícia.
            </p>
          </div>
        ) : (
          <ul className="space-y-2">
            {visible.map((warning) => (
              <WarningCard key={warning.id} warning={warning} />
            ))}
          </ul>
        )}
      </main>
    </div>
  )
}

function WarningCard({ warning }: { warning: Warning }) {
  return (
    <li className={CARD_CLASS}>
      <div className="mb-2 flex items-center gap-2">
        <span
          className={`rounded-full px-2 py-0.5 text-[11px] font-medium ring-1 ${kindBadge(warning.kind)}`}
        >
          {kindLabel(warning.kind)}
        </span>
        <span className="ml-auto text-xs text-muted-foreground">
          {formatRelative(warning.createdAt)}
        </span>
      </div>

      <p className="text-sm leading-relaxed text-foreground">
        {warning.message}
      </p>

      <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1">
        {warning.issuedBy && (
          <span className="text-xs text-muted-foreground">
            Fonte: {warning.issuedBy}
          </span>
        )}
        {warning.url && (
          <a
            href={warning.url}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1 text-xs font-medium text-orange-600 transition-colors hover:text-orange-700 dark:text-orange-400 dark:hover:text-orange-300"
          >
            Ver comunicado
            <ExternalLink className="size-3" aria-hidden />
          </a>
        )}
      </div>
    </li>
  )
}

function ListSkeleton() {
  return (
    <div className="space-y-2">
      {Array.from({ length: 6 }).map((_, i) => (
        <Skeleton key={i} className="h-24 w-full" />
      ))}
    </div>
  )
}
