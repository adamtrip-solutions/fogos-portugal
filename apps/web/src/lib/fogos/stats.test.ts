import { describe, expect, it } from 'vitest'

import {
  alignYoY,
  cumulativeSum,
  dayOfYear,
  formatSignedPercent,
  latestBurnArea,
  yoyRatio,
} from './stats.ts'
import type { DayCount } from './types.ts'

function day(date: string, count: number): DayCount {
  return { date, count }
}

describe('cumulativeSum', () => {
  it('accumulates the running total', () => {
    expect(cumulativeSum([day('2026-01-01', 2), day('2026-01-02', 3), day('2026-01-03', 0)])).toEqual([
      2, 5, 5,
    ])
  })

  it('is empty for an empty series', () => {
    expect(cumulativeSum([])).toEqual([])
  })
})

describe('dayOfYear', () => {
  it('is 1 on Jan 1', () => {
    expect(dayOfYear('2026-01-01')).toBe(1)
  })

  it('counts through the year', () => {
    expect(dayOfYear('2026-02-01')).toBe(32)
    expect(dayOfYear('2025-12-31')).toBe(365)
  })

  it('keeps the same calendar date on the same index across leap years', () => {
    // A leap year must not run ahead: Mar 1 and Dec 31 land on the same index
    // whether or not the year has a Feb 29.
    expect(dayOfYear('2024-03-01')).toBe(dayOfYear('2025-03-01'))
    expect(dayOfYear('2024-03-01')).toBe(60)
    expect(dayOfYear('2024-12-31')).toBe(365)
    expect(dayOfYear('2025-12-31')).toBe(365)
  })

  it('collapses Feb 29 onto the Feb 28 bucket', () => {
    expect(dayOfYear('2024-02-29')).toBe(dayOfYear('2024-02-28'))
    expect(dayOfYear('2024-02-29')).toBe(59)
  })
})

describe('alignYoY', () => {
  it('overlays two years on a shared day-of-year axis, cumulatively', () => {
    const current = [day('2026-01-01', 5), day('2026-01-02', 5)]
    const previous = [day('2025-01-01', 2), day('2025-01-02', 2), day('2025-01-03', 10)]
    const points = alignYoY(current, previous)

    expect(points.map((p) => p.day)).toEqual([1, 2, 3])
    // current cumulative: 5, 10, then no data for day 3.
    expect(points[0].current).toBe(5)
    expect(points[1].current).toBe(10)
    expect(points[2].current).toBeNull()
    // previous cumulative: 2, 4, 14.
    expect(points[0].previous).toBe(2)
    expect(points[1].previous).toBe(4)
    expect(points[2].previous).toBe(14)
  })

  it('aligns the same calendar date to the same axis index', () => {
    const points = alignYoY([day('2026-02-01', 1)], [day('2025-02-01', 9)])
    expect(points).toHaveLength(1)
    expect(points[0].day).toBe(32)
    expect(points[0].current).toBe(1)
    expect(points[0].previous).toBe(9)
  })

  it('aligns across a leap boundary (2025 vs 2024) at Mar 1', () => {
    // 2024 has a Feb 29; the previous-year (2024) Mar 1 must line up with the
    // current-year (2025) Mar 1 rather than drifting a day ahead.
    const current = [day('2025-02-28', 1), day('2025-03-01', 1)]
    const previous = [
      day('2024-02-28', 10),
      day('2024-02-29', 10),
      day('2024-03-01', 10),
    ]
    const points = alignYoY(current, previous)

    // Feb 28 (index 59) and Mar 1 (index 60): Feb 29 collapses onto Feb 28.
    expect(points.map((p) => p.day)).toEqual([59, 60])

    const feb28 = points.find((p) => p.day === 59)!
    const mar1 = points.find((p) => p.day === 60)!
    expect(feb28.current).toBe(1)
    // Previous-year cumulative through Feb 29 lands on the Feb 28 bucket.
    expect(feb28.previous).toBe(20)
    // Both years' Mar 1 share the same axis index.
    expect(mar1.current).toBe(2)
    expect(mar1.previous).toBe(30)
  })

  it('handles empty inputs', () => {
    expect(alignYoY([], [])).toEqual([])
  })
})

describe('latestBurnArea', () => {
  it('returns the last cumulative total', () => {
    expect(
      latestBurnArea([
        { date: '2026-01-01', totalHa: 10 },
        { date: '2026-01-02', totalHa: 42.5 },
      ]),
    ).toBe(42.5)
  })

  it('is 0 when empty', () => {
    expect(latestBurnArea([])).toBe(0)
  })
})

describe('yoyRatio / formatSignedPercent', () => {
  it('computes a signed ratio', () => {
    expect(yoyRatio(125, 100)).toBeCloseTo(0.25)
    expect(yoyRatio(80, 100)).toBeCloseTo(-0.2)
  })

  it('is null without a baseline', () => {
    expect(yoyRatio(10, 0)).toBeNull()
  })

  it('formats signed percentages in PT', () => {
    expect(formatSignedPercent(0.25)).toBe('+25 %')
    expect(formatSignedPercent(-0.12)).toBe('−12 %')
    expect(formatSignedPercent(0)).toBe('0 %')
    expect(formatSignedPercent(null)).toBe('—')
  })
})
