import { useState } from 'react'
import { Check, Droplet, Flame, Siren } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

import { STATUS_BUCKET_COLOR } from '#/lib/fogos/format.ts'
import type { StatusBucket } from '#/lib/fogos/format.ts'
import { useIsMobile } from '#/lib/use-is-mobile.ts'

const ROWS: Array<{ bucket: StatusBucket; Icon: LucideIcon; label: string }> = [
  { bucket: 'dispatch', Icon: Siren, label: 'Despacho' },
  { bucket: 'ongoing', Icon: Flame, label: 'Em curso' },
  { bucket: 'resolving', Icon: Droplet, label: 'Em resolução' },
  { bucket: 'done', Icon: Check, label: 'Concluído' },
]

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/75 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70'
const TITLE_CLASS =
  'text-xs font-semibold uppercase tracking-wider text-muted-foreground'

export function MapLegend() {
  const isMobile = useIsMobile()
  const [open, setOpen] = useState(false)

  // Desktop is always expanded; mobile defaults collapsed and toggles on tap.
  const expanded = !isMobile || open

  return (
    <div className={`${CARD_CLASS} p-3`}>
      {isMobile ? (
        <button
          type="button"
          aria-expanded={open}
          onClick={() => setOpen((v) => !v)}
          className={`${TITLE_CLASS} flex w-full items-center`}
        >
          Legenda
        </button>
      ) : (
        <h2 className={TITLE_CLASS}>Legenda</h2>
      )}

      {expanded && (
        <ul className="mt-2 space-y-1.5">
          {ROWS.map(({ bucket, Icon, label }) => (
            <li key={bucket} className="flex items-center gap-2">
              <span
                className="flex size-[22px] shrink-0 items-center justify-center rounded-full"
                style={{ backgroundColor: STATUS_BUCKET_COLOR[bucket] }}
              >
                <Icon className="size-3 text-white" aria-hidden />
              </span>
              <span className="text-xs text-foreground">{label}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
