import { describe, expect, it } from 'vitest'

import { situationDeltas } from './situation'
import type { SituationReport } from './types'

function report(over: Partial<SituationReport>): SituationReport {
  return {
    id: 'r',
    at: '2025-07-04T09:00:00Z',
    slot: 'morning',
    body: '',
    activeFires: 0,
    totalMan: 0,
    totalTerrain: 0,
    totalAerial: 0,
    topIncidentIds: [],
    ...over,
  }
}

describe('situationDeltas', () => {
  it('marks more active fires as bad and fewer as good', () => {
    const worse = situationDeltas(
      report({ activeFires: 12 }),
      report({ activeFires: 8 }),
    )
    expect(worse).toEqual([{ metric: 'activeFires', diff: 4, tone: 'bad' }])

    const better = situationDeltas(
      report({ activeFires: 5 }),
      report({ activeFires: 9 }),
    )
    expect(better).toEqual([{ metric: 'activeFires', diff: -4, tone: 'good' }])
  })

  it('keeps resource-count changes neutral regardless of direction', () => {
    const deltas = situationDeltas(
      report({ totalMan: 200, totalTerrain: 60, totalAerial: 4 }),
      report({ totalMan: 150, totalTerrain: 70, totalAerial: 4 }),
    )
    expect(deltas).toEqual([
      { metric: 'totalMan', diff: 50, tone: 'neutral' },
      { metric: 'totalTerrain', diff: -10, tone: 'neutral' },
    ])
  })

  it('drops unchanged metrics and preserves tile order', () => {
    const deltas = situationDeltas(
      report({ activeFires: 10, totalMan: 100, totalTerrain: 30, totalAerial: 5 }),
      report({ activeFires: 9, totalMan: 100, totalTerrain: 25, totalAerial: 5 }),
    )
    expect(deltas.map((d) => d.metric)).toEqual(['activeFires', 'totalTerrain'])
  })

  it('returns no deltas when nothing moved', () => {
    const same = report({ activeFires: 3, totalMan: 40 })
    expect(situationDeltas(same, same)).toEqual([])
  })
})
