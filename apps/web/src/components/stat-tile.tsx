import type { LucideIcon } from 'lucide-react'

/**
 * Glass stat tile: a big value with a label and optional icon / delta / hint.
 * Shared by the dashboard header, YoY counters and response-time medians.
 */
export function StatTile({
  label,
  value,
  hint,
  Icon,
  delta,
}: {
  label: string
  value: string
  hint?: string
  Icon?: LucideIcon
  /** Optional signed-change chip, e.g. "+25 %". */
  delta?: { text: string; tone: 'up' | 'down' | 'neutral' }
}) {
  return (
    <div className="rounded-2xl border border-black/5 bg-white/70 p-4 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60">
      <div className="flex items-center justify-between gap-2">
        {Icon && <Icon className="size-4 text-muted-foreground" aria-hidden />}
        {delta && (
          <span
            className={
              delta.tone === 'up'
                ? 'rounded-full bg-red-500/15 px-1.5 py-0.5 text-xs font-semibold text-red-600 dark:text-red-400'
                : delta.tone === 'down'
                  ? 'rounded-full bg-emerald-500/15 px-1.5 py-0.5 text-xs font-semibold text-emerald-600 dark:text-emerald-400'
                  : 'rounded-full bg-muted px-1.5 py-0.5 text-xs font-semibold text-muted-foreground'
            }
          >
            {delta.text}
          </span>
        )}
      </div>
      <div className="mt-2 text-2xl font-bold tabular-nums text-foreground">
        {value}
      </div>
      <div className="text-xs text-muted-foreground">{label}</div>
      {hint && <div className="mt-0.5 text-[11px] text-muted-foreground">{hint}</div>}
    </div>
  )
}
