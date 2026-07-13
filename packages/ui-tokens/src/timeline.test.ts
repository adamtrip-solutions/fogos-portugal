import { describe, expect, it } from 'vitest'

import { buildStatusTimeline } from './timeline'

const at = (iso: string) => `2026-07-10T${iso}:00Z`

describe('buildStatusTimeline', () => {
  it('orders real entries newest → oldest', () => {
    const rows = buildStatusTimeline(
      [
        { at: at('10:00'), code: 5, label: 'Em curso' },
        { at: at('12:00'), code: 7, label: 'Em Resolução' },
        { at: at('11:00'), code: 6, label: 'Chegada ao TO' },
      ],
      at('09:00'),
    )
    expect(rows.map((r) => r.label)).toEqual([
      'Em Resolução',
      'Chegada ao TO',
      'Em curso',
      'Alerta',
    ])
    expect(rows.map((r) => r.synthetic)).toEqual([false, false, false, true])
  })

  it('appends the synthetic Alerta origin at occurredAt as the oldest row', () => {
    const rows = buildStatusTimeline(
      [{ at: at('10:00'), code: 5, label: 'Em curso' }],
      at('09:30'),
    )
    const origin = rows[rows.length - 1]
    expect(origin).toEqual({
      at: at('09:30'),
      code: -1,
      label: 'Alerta',
      synthetic: true,
    })
  })

  it('skips the origin when the earliest observation is at the same instant', () => {
    const rows = buildStatusTimeline(
      [
        { at: at('09:00'), code: 3, label: 'Despacho' },
        { at: at('10:00'), code: 5, label: 'Em curso' },
      ],
      at('09:00'),
    )
    expect(rows).toHaveLength(2)
    expect(rows.every((r) => !r.synthetic)).toBe(true)
  })

  it('renders just the Alerta origin when there is no history', () => {
    const rows = buildStatusTimeline([], at('08:00'))
    expect(rows).toEqual([
      { at: at('08:00'), code: -1, label: 'Alerta', synthetic: true },
    ])
  })
})
