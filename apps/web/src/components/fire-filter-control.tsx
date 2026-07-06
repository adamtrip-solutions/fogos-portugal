import { Check, SlidersHorizontal, X } from 'lucide-react'

import {
  STATUS_BUCKETS,
  STATUS_BUCKET_COLOR,
  STATUS_BUCKET_LABEL,
} from '#/lib/fogos/format.ts'
import type { StatusBucket } from '#/lib/fogos/format.ts'

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/75 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70'
const TITLE_CLASS =
  'text-xs font-semibold uppercase tracking-wider text-muted-foreground'

const CONTROL_TITLE = 'Filtros'

// Single-select "updated within" pills (hours; null = no age limit).
const AGE_OPTIONS: Array<{ label: string; value: number | null }> = [
  { label: 'Tudo', value: null },
  { label: '1h', value: 1 },
  { label: '3h', value: 3 },
  { label: '6h', value: 6 },
  { label: '12h', value: 12 },
]

interface FireFilterControlProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  buckets: ReadonlySet<StatusBucket>
  onBucketsChange: (next: Set<StatusBucket>) => void
  maxAgeHours: number | null
  onMaxAgeChange: (h: number | null) => void
  /** Incidents remaining after filtering (N) out of the pre-filter total (M). */
  visibleCount: number
  totalCount: number
}

/** Whether the current filter differs from the defaults (all buckets, no age). */
function isNonDefault(
  buckets: ReadonlySet<StatusBucket>,
  maxAgeHours: number | null,
): boolean {
  return buckets.size !== STATUS_BUCKETS.length || maxAgeHours != null
}

/**
 * Fully controlled map filter panel (status buckets + activity window),
 * mirroring WeatherLayerControl's collapsed-icon → glass-card pattern.
 */
export function FireFilterControl({
  open,
  onOpenChange,
  buckets,
  onBucketsChange,
  maxAgeHours,
  onMaxAgeChange,
  visibleCount,
  totalCount,
}: FireFilterControlProps) {
  const nonDefault = isNonDefault(buckets, maxAgeHours)

  if (!open) {
    return (
      <button
        type="button"
        aria-label={CONTROL_TITLE}
        onClick={() => onOpenChange(true)}
        className={`${CARD_CLASS} relative flex size-10 items-center justify-center text-zinc-700 transition-colors hover:bg-white/90 dark:text-zinc-200 dark:hover:bg-zinc-900/90`}
      >
        <SlidersHorizontal className="size-[18px]" />
        {nonDefault && (
          <span
            aria-hidden
            className="absolute right-1.5 top-1.5 size-2 rounded-full bg-orange-500 ring-2 ring-white dark:ring-zinc-900"
          />
        )}
      </button>
    )
  }

  const toggleBucket = (bucket: StatusBucket) => {
    const next = new Set(buckets)
    if (next.has(bucket)) next.delete(bucket)
    else next.add(bucket)
    onBucketsChange(next)
  }

  const reset = () => {
    onBucketsChange(new Set(STATUS_BUCKETS))
    onMaxAgeChange(null)
  }

  return (
    <div className={`${CARD_CLASS} w-60 p-3`}>
      <div className="flex items-center justify-between">
        <h2 className={TITLE_CLASS}>{CONTROL_TITLE}</h2>
        <button
          type="button"
          aria-label="Fechar"
          onClick={() => onOpenChange(false)}
          className="-m-1 flex size-6 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
        >
          <X className="size-4" />
        </button>
      </div>

      {/* Estado — multi-select status buckets */}
      <div
        role="group"
        aria-label="Estado"
        className="mt-3 space-y-0.5"
      >
        <p className={`${TITLE_CLASS} px-2 pb-1`}>Estado</p>
        {STATUS_BUCKETS.map((bucket) => {
          const checked = buckets.has(bucket)
          return (
            <button
              key={bucket}
              type="button"
              role="checkbox"
              aria-checked={checked}
              onClick={() => toggleBucket(bucket)}
              className={`flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-xs transition-colors hover:bg-black/5 dark:hover:bg-white/10 ${
                checked ? '' : 'opacity-50'
              }`}
            >
              <span
                aria-hidden
                className="size-2 shrink-0 rounded-full"
                style={{ backgroundColor: STATUS_BUCKET_COLOR[bucket] }}
              />
              <span className="text-foreground">
                {STATUS_BUCKET_LABEL[bucket]}
              </span>
              {checked && (
                <Check className="ml-auto size-3.5 shrink-0 text-orange-500" />
              )}
            </button>
          )
        })}
      </div>

      {/* Atividade — single-select "updated within" */}
      <div className="mt-3 border-t border-black/5 pt-3 dark:border-white/10">
        <p className={`${TITLE_CLASS} px-2`}>Atividade</p>
        <p className="px-2 pb-2 pt-1 text-[11px] text-muted-foreground">
          Atualizadas há menos de
        </p>
        <div className="flex flex-wrap gap-1.5 px-2">
          {AGE_OPTIONS.map((opt) => {
            const selected = maxAgeHours === opt.value
            return (
              <button
                key={opt.label}
                type="button"
                aria-pressed={selected}
                onClick={() => onMaxAgeChange(opt.value)}
                className={
                  selected
                    ? 'rounded-full bg-orange-500/15 px-2.5 py-1 text-xs font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
                    : 'rounded-full bg-muted/60 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted'
                }
              >
                {opt.label}
              </button>
            )
          })}
        </div>
      </div>

      {/* Footer — visible count + reset */}
      <div className="mt-3 flex items-center justify-between gap-2 border-t border-black/5 pt-3 dark:border-white/10">
        <span className="text-xs tabular-nums text-muted-foreground">
          {visibleCount} de {totalCount} visíveis
        </span>
        {nonDefault && (
          <button
            type="button"
            onClick={reset}
            className="text-xs font-medium text-orange-600 transition-colors hover:text-orange-700 dark:text-orange-400 dark:hover:text-orange-300"
          >
            Repor
          </button>
        )}
      </div>
    </div>
  )
}
