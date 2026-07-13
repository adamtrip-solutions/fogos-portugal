// Pure data-shaping helpers for the season statistics dashboard. Ported verbatim
// from apps/web/src/lib/fogos/stats.ts (the data-shaping half — the display
// formatters live in @fogos/ui-tokens). Deliberately free of React / chart libs
// so they stay unit-testable in the node vitest environment.

import type { DayArea, DayCount } from './types'

/** Running total of the `count` field, in the order given. */
export function cumulativeSum(days: readonly DayCount[]): number[] {
  const out: number[] = []
  let total = 0
  for (const d of days) {
    total += d.count
    out.push(total)
  }
  return out
}

/**
 * Calendar-day index (1-based) for an ISO `YYYY-MM-DD` date, computed from its
 * month/day on a fixed common (non-leap) reference year. This keeps the same
 * calendar date on the same index regardless of leap years — without this a
 * leap year runs one day ahead of a common year after Feb 29, shifting the
 * previous-year curve in the YoY overlay. Feb 29 maps onto the Feb 28 bucket so
 * common-year charts stay a clean 365 points. UTC avoids TZ drift on a
 * date-only value.
 */
export function dayOfYear(isoDate: string): number {
  const d = new Date(`${isoDate}T00:00:00Z`)
  const month = d.getUTCMonth()
  const day = d.getUTCDate()
  // Collapse Feb 29 onto Feb 28 so leap and common years share a 365-day axis.
  const refDay = month === 1 && day === 29 ? 28 : day
  const ref = Date.UTC(2001, month, refDay) // 2001 is a common year
  const start = Date.UTC(2001, 0, 1)
  return Math.floor((ref - start) / 86_400_000) + 1
}

/** One aligned row of the year-over-year cumulative ignition overlay. */
export interface YoYPoint {
  /** Day of year, 1..366 — the shared X axis. */
  day: number
  /** Cumulative ignitions in the current year up to this day, or null past its data. */
  current: number | null
  /** Cumulative ignitions in the previous year up to this day, or null. */
  previous: number | null
}

/**
 * Aligns two `ignitionsByDay` series onto a shared day-of-year X axis and
 * cumulative-sums each, so the current year can be overlaid on the previous one
 * regardless of differing lengths (the current year stops at today). Days
 * present in only one series carry null for the other, letting the line stop.
 */
export function alignYoY(
  current: readonly DayCount[],
  previous: readonly DayCount[],
): YoYPoint[] {
  const curCum = cumulativeSum(current)
  const prevCum = cumulativeSum(previous)

  const byDay = new Map<number, YoYPoint>()
  const ensure = (day: number): YoYPoint => {
    let p = byDay.get(day)
    if (!p) {
      p = { day, current: null, previous: null }
      byDay.set(day, p)
    }
    return p
  }

  current.forEach((d, i) => {
    ensure(dayOfYear(d.date)).current = curCum[i]
  })
  previous.forEach((d, i) => {
    ensure(dayOfYear(d.date)).previous = prevCum[i]
  })

  return [...byDay.values()].sort((a, b) => a.day - b.day)
}

/** Latest cumulative burn-area total (ha), or 0 when the series is empty. */
export function latestBurnArea(series: readonly DayArea[]): number {
  return series.length > 0 ? series[series.length - 1].totalHa : 0
}

/** Year-over-year delta as a signed ratio (e.g. 0.25 = +25%), null when no baseline. */
export function yoyRatio(current: number, previous: number): number | null {
  if (previous <= 0) return null
  return (current - previous) / previous
}
