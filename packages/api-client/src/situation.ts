// Pure data-shaping for the /situacao screen: the per-metric delta of a report
// against the previous one. Ported verbatim from the web app's situation route
// (apps/web/src/routes/situacao.tsx `makeDelta`/`deltasFor`). The display
// formatting (signed-integer text) lives in @fogos/ui-tokens; the tone semantics
// live here because they are data, not presentation. No React / chart libs.

import type { SituationReport } from './types'

/** The four headline metrics that carry a delta chip, in tile order. */
export type SituationMetric =
  | 'activeFires'
  | 'totalMan'
  | 'totalTerrain'
  | 'totalAerial'

/** Chip tone — mirrors the mobile StatTile / web delta-chip tones. */
export type SituationDeltaTone = 'good' | 'bad' | 'neutral'

/** One metric's non-zero change against the previous report. */
export interface SituationDelta {
  metric: SituationMetric
  /** Signed difference `curr - prev`; never 0 (unchanged metrics are dropped). */
  diff: number
  tone: SituationDeltaTone
}

// More active fires is bad, fewer is good; resource counts are directionally
// neutral (more or fewer means nothing without context) — exactly as web.
const METRICS: Array<{ metric: SituationMetric; kind: 'fires' | 'resource' }> = [
  { metric: 'activeFires', kind: 'fires' },
  { metric: 'totalMan', kind: 'resource' },
  { metric: 'totalTerrain', kind: 'resource' },
  { metric: 'totalAerial', kind: 'resource' },
]

/**
 * Per-metric deltas of `curr` versus `prev`, in tile order, dropping metrics
 * that did not move. Returns an empty array when nothing changed. Mirrors web's
 * `deltasFor`: fires get good/bad tone by direction, resources stay neutral.
 */
export function situationDeltas(
  curr: SituationReport,
  prev: SituationReport,
): SituationDelta[] {
  const out: SituationDelta[] = []
  for (const { metric, kind } of METRICS) {
    const diff = curr[metric] - prev[metric]
    if (diff === 0) continue
    const tone: SituationDeltaTone =
      kind === 'fires' ? (diff > 0 ? 'bad' : 'good') : 'neutral'
    out.push({ metric, diff, tone })
  }
  return out
}
