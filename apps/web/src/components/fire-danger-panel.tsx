import { Info } from 'lucide-react'

import {
  FIRE_DANGER_CLASSES,
  FIRE_DANGER_DAY_LABELS,
  FWI_CREDIT,
  FWI_HELP_TEXT,
  FWI_LAYER_LABEL,
} from '#/lib/weather/effis.ts'
import { useIsMobile } from '#/lib/use-is-mobile.ts'

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/75 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70'
const TITLE_CLASS =
  'text-xs font-semibold uppercase tracking-wider text-muted-foreground'

interface FireDangerPanelProps {
  /** Forecast day offset: 0 = today, 1 = tomorrow, 2 = +2 days. */
  day: number
  onDayChange: (day: number) => void
}

/**
 * On-map controls for the EFFIS FWI overlay: a forecast-day segmented control
 * plus a compact danger-class legend. Rendered only while the layer is active
 * (see index.tsx), floating above the fire-status legend at bottom-left.
 */
export function FireDangerPanel({ day, onDayChange }: FireDangerPanelProps) {
  const isMobile = useIsMobile()

  return (
    <div className={`${CARD_CLASS} max-w-[calc(100vw-2rem)] p-3`}>
      <div className="flex items-center gap-1.5">
        <h2 className={TITLE_CLASS}>{FWI_LAYER_LABEL}</h2>
        <span
          className="text-muted-foreground/70"
          title={FWI_HELP_TEXT}
          aria-label={FWI_HELP_TEXT}
        >
          <Info className="size-3.5" aria-hidden />
        </span>
      </div>

      {/* Forecast day — segmented single-select chips. */}
      <div
        role="radiogroup"
        aria-label="Dia de previsão"
        className="mt-2 flex gap-1.5"
      >
        {FIRE_DANGER_DAY_LABELS.map((label, i) => {
          const selected = day === i
          return (
            <button
              key={label}
              type="button"
              role="radio"
              aria-checked={selected}
              onClick={() => onDayChange(i)}
              className={
                selected
                  ? 'rounded-full bg-orange-500/15 px-2.5 py-1 text-xs font-medium text-orange-700 ring-1 ring-orange-500/40 dark:text-orange-300'
                  : 'rounded-full bg-muted/60 px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:bg-muted'
              }
            >
              {label}
            </button>
          )
        })}
      </div>

      {/* Danger-class legend. Mobile: a single contiguous colour bar with end
          labels. Desktop: a labelled list with the FWI value ranges. */}
      {isMobile ? (
        <div className="mt-2.5">
          <div
            className="flex h-2 overflow-hidden rounded-full"
            role="img"
            aria-label="Escala de perigo: de muito baixo a extremo"
          >
            {FIRE_DANGER_CLASSES.map((c) => (
              <span
                key={c.label}
                className="flex-1"
                style={{ backgroundColor: c.color }}
              />
            ))}
          </div>
          <div className="mt-1 flex justify-between text-[10px] text-muted-foreground">
            <span>Muito baixo</span>
            <span>Extremo</span>
          </div>
        </div>
      ) : (
        <ul className="mt-2.5 space-y-1">
          {FIRE_DANGER_CLASSES.map((c) => (
            <li key={c.label} className="flex items-center gap-2">
              <span
                className="size-3 shrink-0 rounded-[3px]"
                style={{ backgroundColor: c.color }}
                aria-hidden
              />
              <span className="text-xs text-foreground">{c.label}</span>
              <span className="ml-auto text-[11px] tabular-nums text-muted-foreground">
                {c.range}
              </span>
            </li>
          ))}
        </ul>
      )}

      <p className="mt-2.5 text-[11px] text-muted-foreground">{FWI_CREDIT}</p>
    </div>
  )
}
