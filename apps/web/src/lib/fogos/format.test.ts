import { describe, expect, it } from 'vitest'

import {
  CRITICAL_REASON_LABELS,
  compassBearing,
  criticalReasonLabel,
  formatDuration,
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
