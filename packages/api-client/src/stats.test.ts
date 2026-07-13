import { describe, expect, it } from 'vitest'

import {
  alignYoY,
  cumulativeSum,
  dayOfYear,
  latestBurnArea,
  yoyRatio,
} from './stats'
import type { DayArea, DayCount } from './types'

describe('cumulativeSum', () => {
  it('produces a running total in order', () => {
    const days: DayCount[] = [
      { date: '2025-01-01', count: 2 },
      { date: '2025-01-02', count: 3 },
      { date: '2025-01-03', count: 0 },
      { date: '2025-01-04', count: 5 },
    ]
    expect(cumulativeSum(days)).toEqual([2, 5, 5, 10])
  })

  it('returns an empty array for no days', () => {
    expect(cumulativeSum([])).toEqual([])
  })
})

describe('dayOfYear', () => {
  it('is 1 on Jan 1 and 59 on Feb 28', () => {
    expect(dayOfYear('2025-01-01')).toBe(1)
    expect(dayOfYear('2025-02-28')).toBe(59)
  })

  it('keeps the same calendar date on the same index across leap years', () => {
    // Mar 1 must land on the same index in a leap year (2024) and a common
    // year (2025), otherwise the YoY overlay drifts by a day after Feb 29.
    expect(dayOfYear('2024-03-01')).toBe(dayOfYear('2025-03-01'))
    expect(dayOfYear('2024-12-31')).toBe(dayOfYear('2025-12-31'))
  })

  it('collapses Feb 29 onto the Feb 28 bucket', () => {
    expect(dayOfYear('2024-02-29')).toBe(dayOfYear('2024-02-28'))
  })
})

describe('alignYoY', () => {
  it('overlays cumulative current and previous on a shared day axis', () => {
    const current: DayCount[] = [
      { date: '2025-01-01', count: 1 },
      { date: '2025-01-02', count: 2 },
    ]
    const previous: DayCount[] = [
      { date: '2024-01-01', count: 3 },
      { date: '2024-01-02', count: 1 },
      { date: '2024-01-03', count: 4 },
    ]
    expect(alignYoY(current, previous)).toEqual([
      { day: 1, current: 1, previous: 3 },
      { day: 2, current: 3, previous: 4 },
      { day: 3, current: null, previous: 8 },
    ])
  })

  it('sorts by day and leaves gaps null', () => {
    const rows = alignYoY(
      [{ date: '2025-02-01', count: 5 }],
      [{ date: '2024-01-01', count: 2 }],
    )
    expect(rows.map((r) => r.day)).toEqual([1, 32])
    expect(rows[0]).toEqual({ day: 1, current: null, previous: 2 })
    expect(rows[1]).toEqual({ day: 32, current: 5, previous: null })
  })

  it('is empty when both series are empty', () => {
    expect(alignYoY([], [])).toEqual([])
  })
})

describe('latestBurnArea', () => {
  it('returns the last cumulative total', () => {
    const series: DayArea[] = [
      { date: '2025-01-01', totalHa: 10 },
      { date: '2025-01-02', totalHa: 42.5 },
    ]
    expect(latestBurnArea(series)).toBe(42.5)
  })

  it('returns 0 for an empty series', () => {
    expect(latestBurnArea([])).toBe(0)
  })
})

describe('yoyRatio', () => {
  it('computes a signed ratio versus the baseline', () => {
    expect(yoyRatio(125, 100)).toBeCloseTo(0.25)
    expect(yoyRatio(80, 100)).toBeCloseTo(-0.2)
  })

  it('returns null when there is no positive baseline', () => {
    expect(yoyRatio(10, 0)).toBeNull()
    expect(yoyRatio(10, -5)).toBeNull()
  })
})
