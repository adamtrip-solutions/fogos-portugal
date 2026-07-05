// Pure helpers for the season statistics dashboard. Deliberately free of React /
// Recharts so they stay unit-testable in the node vitest environment.

import type { DayArea, DayCount } from './types.ts'

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

const monthDayFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
})

/**
 * Formats a day-of-year index (1-based) back into a `d mmm` tick label. Uses a
 * fixed non-leap reference year so ticks read the same across the overlay.
 */
export function dayOfYearLabel(day: number): string {
  const ms = Date.UTC(2025, 0, 1) + (day - 1) * 86_400_000
  return monthDayFmt.format(new Date(ms))
}

const integerFmt = new Intl.NumberFormat('pt-PT', { maximumFractionDigits: 0 })

/** e.g. "1 234". */
export function formatInteger(value: number): string {
  return integerFmt.format(value)
}

const percentFmt = new Intl.NumberFormat('pt-PT', {
  style: 'percent',
  maximumFractionDigits: 1,
})

/** e.g. "12,5 %". */
export function formatPercent(fraction: number): string {
  return percentFmt.format(fraction)
}

/** e.g. "+25 %" / "−12 %" / "—" for a signed YoY ratio (rounded to whole %). */
export function formatSignedPercent(ratio: number | null): string {
  if (ratio == null || !Number.isFinite(ratio)) return '—'
  const pct = Math.round(ratio * 100)
  const sign = pct > 0 ? '+' : pct < 0 ? '−' : ''
  return `${sign}${Math.abs(pct)} %`
}

/** 24-hour label like "09h" for the hourly histogram. */
export function hourLabel(hour: number): string {
  return `${String(hour).padStart(2, '0')}h`
}
