import type { ConcelhoRiskDay } from '#/lib/fogos/types.ts'

// Fire-risk level → color + PT-friendly semantics (IPMA 1–5 scale). Single
// source of truth for the risk palette, shared by the concelho profile strip
// and the /risco national map. Kept in sync with the backend RiskLevels labels.
export const RISK_STYLE: Record<number, { bg: string; label: string }> = {
  1: { bg: '#6ABF59', label: 'Reduzido' },
  2: { bg: '#C6D82F', label: 'Moderado' },
  3: { bg: '#F5B301', label: 'Elevado' },
  4: { bg: '#FF6E02', label: 'Muito elevado' },
  5: { bg: '#B81E1F', label: 'Máximo' },
}

/** Neutral swatch for an unknown / missing level. */
export const RISK_UNKNOWN = { bg: '#BDBDBD', label: '—' }

/** The five levels in order, for legends and map colour ramps. */
export const RISK_LEVELS = [1, 2, 3, 4, 5] as const

export function riskStyle(level: number) {
  return RISK_STYLE[level] ?? RISK_UNKNOWN
}

/**
 * The 5-day per-concelho risk strip: one chip per horizon with its level colour
 * and PT label. Reused by the concelho profile page and the /risco detail card.
 */
export function RiskStrip({ risk }: { risk: ConcelhoRiskDay[] }) {
  const dayFmt = new Intl.DateTimeFormat('pt-PT', { weekday: 'short', day: 'numeric' })
  return (
    <div className="grid grid-cols-5 gap-2">
      {risk.slice(0, 5).map((r) => {
        const style = riskStyle(r.level)
        return (
          <div
            key={r.date}
            className="flex flex-col items-center gap-1.5 rounded-xl border border-black/5 bg-white/70 p-2 text-center dark:border-white/10 dark:bg-zinc-900/60"
          >
            <span className="text-[11px] font-medium text-muted-foreground">
              {dayFmt.format(new Date(`${r.date}T00:00:00`))}
            </span>
            <span
              className="flex size-9 items-center justify-center rounded-full text-sm font-bold text-white"
              style={{ backgroundColor: style.bg }}
              title={r.label}
            >
              {r.level}
            </span>
            <span className="text-[10px] leading-tight text-muted-foreground">
              {r.label || style.label}
            </span>
          </div>
        )
      })}
    </div>
  )
}
