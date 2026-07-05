import { describe, expect, it } from 'vitest'

import { alertKindTitle, latestCreatedAt, newEvents } from './alerts.ts'
import type { AlertEvent } from './types.ts'

function ev(id: string, createdAt: string, kind = 'NEW_INCIDENT'): AlertEvent {
  return { id, kind, incidentId: null, message: `msg ${id}`, createdAt }
}

describe('newEvents', () => {
  // API returns newest-first.
  const events = [
    ev('c', '2026-07-04T12:00:00Z'),
    ev('b', '2026-07-04T11:00:00Z'),
    ev('a', '2026-07-04T10:00:00Z'),
  ]

  it('returns everything (oldest→newest) when nothing has been seen', () => {
    expect(newEvents(events, null).map((e) => e.id)).toEqual(['a', 'b', 'c'])
  })

  it('returns only events strictly newer than the seen cursor', () => {
    expect(newEvents(events, '2026-07-04T11:00:00Z').map((e) => e.id)).toEqual(['c'])
  })

  it('returns nothing when the cursor is at or past the newest', () => {
    expect(newEvents(events, '2026-07-04T12:00:00Z')).toEqual([])
  })

  it('de-duplicates by id', () => {
    const dupes = [ev('x', '2026-07-04T10:00:00Z'), ev('x', '2026-07-04T10:00:00Z')]
    expect(newEvents(dupes, null)).toHaveLength(1)
  })

  it('is empty for no events', () => {
    expect(newEvents([], null)).toEqual([])
  })
})

describe('latestCreatedAt', () => {
  it('finds the newest timestamp regardless of order', () => {
    expect(
      latestCreatedAt([
        ev('a', '2026-07-04T10:00:00Z'),
        ev('c', '2026-07-04T12:00:00Z'),
        ev('b', '2026-07-04T11:00:00Z'),
      ]),
    ).toBe('2026-07-04T12:00:00Z')
  })

  it('is null when empty', () => {
    expect(latestCreatedAt([])).toBeNull()
  })
})

describe('alertKindTitle', () => {
  it('maps known kinds to PT titles', () => {
    expect(alertKindTitle('NEW_INCIDENT')).toBe('Novo incêndio')
    expect(alertKindTitle('ESCALATION')).toBe('Ocorrência em escalada')
    expect(alertKindTitle('REKINDLE')).toBe('Reacendimento')
    expect(alertKindTitle('RISK')).toBe('Risco de incêndio')
  })

  it('falls back for unknown kinds', () => {
    expect(alertKindTitle('WHATEVER')).toBe('Alerta')
  })
})
