import { describe, expect, it } from 'vitest'

import { normalizeIncidentParam } from './search.ts'

describe('normalizeIncidentParam', () => {
  it('keeps a non-empty string id', () => {
    expect(normalizeIncidentParam('2026070400004')).toBe('2026070400004')
    expect(normalizeIncidentParam('abc123')).toBe('abc123')
  })

  it('coerces a numeric id to a string', () => {
    // The default search parser JSON-decodes `?incident=2026070400004` into a
    // number — this coercion is what stops the param being dropped (307).
    expect(normalizeIncidentParam(2026070400004)).toBe('2026070400004')
    expect(normalizeIncidentParam(123)).toBe('123')
  })

  it('drops empty / missing / invalid values', () => {
    expect(normalizeIncidentParam('')).toBeUndefined()
    expect(normalizeIncidentParam(undefined)).toBeUndefined()
    expect(normalizeIncidentParam(null)).toBeUndefined()
    expect(normalizeIncidentParam(Number.NaN)).toBeUndefined()
    expect(normalizeIncidentParam({})).toBeUndefined()
    expect(normalizeIncidentParam(true)).toBeUndefined()
  })
})
