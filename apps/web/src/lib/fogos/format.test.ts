import { describe, expect, it } from 'vitest'

import {
  CRITICAL_REASON_LABELS,
  DEMOBILIZED_HIDE_HOURS,
  STALE_HIDE_HOURS,
  compassBearing,
  criticalReasonLabel,
  formatDuration,
  isHiddenFromMap,
  isOngoingStatus,
  statusBucket,
  statusColorForCode,
} from './format.ts'

describe('formatDuration', () => {
  it('formats hours and minutes', () => {
    expect(formatDuration(3600 + 23 * 60)).toBe('1 h 23 min')
  })

  it('drops the minutes component when it is zero', () => {
    expect(formatDuration(2 * 3600)).toBe('2 h')
  })

  it('formats minutes-only durations', () => {
    expect(formatDuration(45 * 60)).toBe('45 min')
  })

  it('collapses sub-minute durations', () => {
    expect(formatDuration(30)).toBe('< 1 min')
    expect(formatDuration(0)).toBe('< 1 min')
  })

  it('clamps negative input to zero', () => {
    expect(formatDuration(-100)).toBe('< 1 min')
  })

  it('floors partial minutes rather than rounding up', () => {
    // 1 h 23 min 59 s → "1 h 23 min", never "1 h 24 min".
    expect(formatDuration(3600 + 23 * 60 + 59)).toBe('1 h 23 min')
  })
})

describe('criticalReasonLabel', () => {
  it('translates every known key to European Portuguese', () => {
    expect(criticalReasonLabel('TEMP_ABOVE_30')).toBe('Temperatura > 30 °C')
    expect(criticalReasonLabel('HUMIDITY_BELOW_30')).toBe('Humidade < 30%')
    expect(criticalReasonLabel('WIND_ABOVE_30')).toBe('Vento > 30 km/h')
    expect(criticalReasonLabel('RISK_MAXIMUM')).toBe('Risco máximo')
    expect(criticalReasonLabel('HEAT_WAVE')).toBe('Onda de calor')
  })

  it('covers exactly the five spec keys', () => {
    expect(Object.keys(CRITICAL_REASON_LABELS).sort()).toEqual(
      [
        'HEAT_WAVE',
        'HUMIDITY_BELOW_30',
        'RISK_MAXIMUM',
        'TEMP_ABOVE_30',
        'WIND_ABOVE_30',
      ].sort(),
    )
  })

  it('falls back to the raw key when unknown', () => {
    expect(criticalReasonLabel('SOMETHING_ELSE')).toBe('SOMETHING_ELSE')
  })
})

describe('status code 13 (feed-drop close-out)', () => {
  it('is finished, not ongoing, so it time-windows out like Encerrada', () => {
    expect(isOngoingStatus(13)).toBe(false)
    expect(isOngoingStatus(10)).toBe(false) // mirrors Encerrada
    expect(isOngoingStatus(5)).toBe(true)
  })

  it('reads gray (Concluído family), like Encerrada', () => {
    expect(statusColorForCode(13)).toBe(statusColorForCode(10))
    expect(statusColorForCode(13)).toBe('#BDBDBD')
  })

  it('buckets as done, mirroring Encerrada', () => {
    expect(statusBucket(13)).toBe('done')
    expect(statusBucket(13)).toBe(statusBucket(10))
  })
})

describe('statusColorForCode (bucket palette)', () => {
  it('keeps Em Resolução green and paints Vigilância blue', () => {
    expect(statusColorForCode(7)).toBe('#6ABF59')
    expect(statusColorForCode(9)).toBe('#1E88E5')
  })
})

describe('isHiddenFromMap', () => {
  const NOW = Date.parse('2026-08-01T12:00:00Z')
  const hoursAgo = (h: number) => new Date(NOW - h * 3_600_000).toISOString()

  const fire = (o: {
    code: number
    demobilizedSince?: string | null
    statusChangedAt?: string | null
    occurredAt?: string
    /** Defaults to 0 (unmanned) — the population the hide rules target. */
    man?: number
  }) => ({
    status: { code: o.code },
    statusChangedAt: 'statusChangedAt' in o ? o.statusChangedAt! : hoursAgo(1),
    occurredAt: o.occurredAt ?? hoursAgo(1),
    resources: { man: o.man ?? 0 },
    signals: { demobilizedSince: o.demobilizedSince },
  })

  it('never hides fires outside the resolving/vigilância buckets', () => {
    for (const code of [3, 4, 5, 6, 8, 10, 11, 12, 13]) {
      // Even with a long-ago demobilization and a stale status, other buckets stay.
      const inc = fire({
        code,
        demobilizedSince: hoursAgo(48),
        statusChangedAt: hoursAgo(72),
        occurredAt: hoursAgo(72),
      })
      expect(isHiddenFromMap(inc, NOW)).toBe(false)
    }
  })

  it('hides a resolving fire demobilized ≥ 12h ago', () => {
    const inc = fire({ code: 7, demobilizedSince: hoursAgo(DEMOBILIZED_HIDE_HOURS) })
    expect(isHiddenFromMap(inc, NOW)).toBe(true)
  })

  it('hides a vigilância fire demobilized ≥ 12h ago', () => {
    const inc = fire({ code: 9, demobilizedSince: hoursAgo(13) })
    expect(isHiddenFromMap(inc, NOW)).toBe(true)
  })

  it('keeps a resolving fire demobilized less than 12h ago', () => {
    const inc = fire({ code: 7, demobilizedSince: hoursAgo(11) })
    expect(isHiddenFromMap(inc, NOW)).toBe(false)
  })

  it('keeps a resolving fire that is not demobilized (null) and fresh', () => {
    const inc = fire({ code: 7, demobilizedSince: null, statusChangedAt: hoursAgo(2) })
    expect(isHiddenFromMap(inc, NOW)).toBe(false)
  })

  it('tolerates the demobilizedSince field being absent (older API)', () => {
    const inc = {
      status: { code: 7 },
      statusChangedAt: hoursAgo(2),
      occurredAt: hoursAgo(2),
      resources: { man: 0 },
      signals: {}, // no demobilizedSince key at all
    }
    expect(isHiddenFromMap(inc, NOW)).toBe(false)
  })

  it('hides an unmanned (man 0) resolving fire whose status last changed ≥ 24h ago (stale)', () => {
    const inc = fire({ code: 7, demobilizedSince: null, statusChangedAt: hoursAgo(STALE_HIDE_HOURS), man: 0 })
    expect(isHiddenFromMap(inc, NOW)).toBe(true)
  })

  it('hides an unknown-crew (man -1) stale fire — the sentinel counts as unmanned for the stale rule', () => {
    const inc = fire({ code: 9, demobilizedSince: null, statusChangedAt: hoursAgo(30), man: -1 })
    expect(isHiddenFromMap(inc, NOW)).toBe(true)
  })

  it('keeps a currently-crewed fire visible however stale its status is', () => {
    // A staffed long-running em resolução fire must never hide on staleness alone.
    const inc = fire({ code: 7, demobilizedSince: null, statusChangedAt: hoursAgo(72), man: 4 })
    expect(isHiddenFromMap(inc, NOW)).toBe(false)
  })

  it('keeps a fresh vigilância fire just under the 24h stale threshold', () => {
    const inc = fire({ code: 9, demobilizedSince: null, statusChangedAt: hoursAgo(23) })
    expect(isHiddenFromMap(inc, NOW)).toBe(false)
  })

  it('falls back to occurredAt for staleness when statusChangedAt is null', () => {
    const inc = fire({ code: 9, demobilizedSince: null, statusChangedAt: null, occurredAt: hoursAgo(30) })
    expect(isHiddenFromMap(inc, NOW)).toBe(true)
  })

  it('does not key staleness on updatedAt (ICNF enrichment pollutes it)', () => {
    // A fire whose record was re-enriched moments ago (implicit: we never read
    // updatedAt) but whose status has not changed in 30h is still stale.
    const inc = fire({ code: 7, demobilizedSince: null, statusChangedAt: hoursAgo(30) })
    expect(isHiddenFromMap(inc, NOW)).toBe(true)
  })
})

describe('compassBearing', () => {
  it('maps cardinal points to degrees', () => {
    expect(compassBearing('N')).toBe(0)
    expect(compassBearing('E')).toBe(90)
    expect(compassBearing('S')).toBe(180)
  })

  it('handles Portuguese west/north-west tokens', () => {
    expect(compassBearing('O')).toBe(270)
    expect(compassBearing('NO')).toBe(315)
  })

  it('is case- and whitespace-insensitive', () => {
    expect(compassBearing(' ne ')).toBe(45)
  })

  it('returns null for unknown or empty input', () => {
    expect(compassBearing(null)).toBeNull()
    expect(compassBearing('')).toBeNull()
    expect(compassBearing('xyz')).toBeNull()
  })
})
