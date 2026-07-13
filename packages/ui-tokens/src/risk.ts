// Fire-risk level → color + pt-PT label (IPMA 1–5 scale). Ported verbatim from
// the web app's risk-strip.tsx palette — the single source of truth for the risk
// colours, shared by the /risco national choropleth and the concelho profile's
// 5-day strip. Kept in sync with the backend RiskLevels labels. No React / RN
// imports.

/** Level → fill colour + pt-PT semantics (1–5). */
export const RISK_STYLE: Record<number, { bg: string; label: string }> = {
  1: { bg: '#6ABF59', label: 'Reduzido' },
  2: { bg: '#C6D82F', label: 'Moderado' },
  3: { bg: '#F5B301', label: 'Elevado' },
  4: { bg: '#FF6E02', label: 'Muito elevado' },
  5: { bg: '#B81E1F', label: 'Máximo' },
}

/** Neutral swatch + label for an unknown / missing level (0 or out of range). */
export const RISK_UNKNOWN = { bg: '#BDBDBD', label: 'Sem dados' }

/** The five levels in order, for legends and map colour ramps. */
export const RISK_LEVELS = [1, 2, 3, 4, 5] as const

/** Style bag for a level, falling back to the neutral unknown swatch. */
export function riskStyle(level: number): { bg: string; label: string } {
  return RISK_STYLE[level] ?? RISK_UNKNOWN
}

/** pt-PT label for a level; "Sem dados" for 0 / out of range. */
export function riskLabel(level: number): string {
  return level >= 1 && level <= 5 ? RISK_STYLE[level].label : RISK_UNKNOWN.label
}
