import { describe, expect, it } from 'vitest'

import {
  DEMOBILIZED_HIDE_HOURS,
  DISPLAY_WINDOW_HOURS,
  STALE_HIDE_HOURS,
  isDisplayedOnMap,
  type DisplayWindowIncident,
} from './display-window'

const NOW = Date.parse('2026-07-10T12:00:00Z')
const HOUR = 60 * 60 * 1000
const hoursAgo = (h: number) => new Date(NOW - h * HOUR).toISOString()

/** Build an incident with sensible defaults; override per case. */
function incident(over: {
  code: number
  statusChangedAt?: string | null
  occurredAt?: string
  man?: number
  demobilizedSince?: string | null
}): DisplayWindowIncident {
  return {
    status: { code: over.code },
    // Respect an explicit `null` (don't let `??` fall back to a default).
    statusChangedAt: 'statusChangedAt' in over ? over.statusChangedAt! : hoursAgo(1),
    occurredAt: over.occurredAt ?? hoursAgo(1),
    resources: { man: over.man ?? 5 },
    signals: { demobilizedSince: over.demobilizedSince },
  }
}

describe('isDisplayedOnMap', () => {
  it('shows active fires (3–6) regardless of age', () => {
    for (const code of [3, 4, 5, 6]) {
      expect(
        isDisplayedOnMap(
          incident({ code, statusChangedAt: hoursAgo(999), occurredAt: hoursAgo(999) }),
          NOW,
        ),
      ).toBe(true)
    }
  })

  it('shows finished (done) fires within the display window', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 8, statusChangedAt: hoursAgo(DISPLAY_WINDOW_HOURS - 0.5) }),
        NOW,
      ),
    ).toBe(true)
  })

  it('hides finished (done) fires past the display window', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 8, statusChangedAt: hoursAgo(DISPLAY_WINDOW_HOURS + 0.5) }),
        NOW,
      ),
    ).toBe(false)
  })

  it('falls back to occurredAt for the finished window when statusChangedAt is null', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 8, statusChangedAt: null, occurredAt: hoursAgo(1) }),
        NOW,
      ),
    ).toBe(true)
    expect(
      isDisplayedOnMap(
        incident({ code: 8, statusChangedAt: null, occurredAt: hoursAgo(10) }),
        NOW,
      ),
    ).toBe(false)
  })

  it('shows Em Resolução (7) / Vigilância (9) that are fresh and crewed', () => {
    for (const code of [7, 9]) {
      expect(isDisplayedOnMap(incident({ code, man: 3 }), NOW)).toBe(true)
    }
  })

  // ── Demobilized (12h) branch ──────────────────────────────────────────────
  it('hides 7/9 when demobilizedSince is ≥12h old', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 7, demobilizedSince: hoursAgo(DEMOBILIZED_HIDE_HOURS) }),
        NOW,
      ),
    ).toBe(false)
  })

  it('keeps 7/9 when demobilizedSince is <12h old', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 9, demobilizedSince: hoursAgo(DEMOBILIZED_HIDE_HOURS - 1) }),
        NOW,
      ),
    ).toBe(true)
  })

  it('treats missing/undefined/null demobilizedSince as null (branch dark)', () => {
    expect(isDisplayedOnMap(incident({ code: 7 }), NOW)).toBe(true)
    expect(
      isDisplayedOnMap(incident({ code: 7, demobilizedSince: null }), NOW),
    ).toBe(true)
    // Field entirely absent from signals object:
    expect(
      isDisplayedOnMap(
        {
          status: { code: 7 },
          statusChangedAt: hoursAgo(1),
          occurredAt: hoursAgo(1),
          resources: { man: 5 },
          signals: {},
        },
        NOW,
      ),
    ).toBe(true)
  })

  // ── Stale (24h) + unmanned branch ─────────────────────────────────────────
  it('hides 7/9 that are stale (statusChangedAt ≥24h) AND unmanned (man 0)', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 7, statusChangedAt: hoursAgo(STALE_HIDE_HOURS), man: 0 }),
        NOW,
      ),
    ).toBe(false)
  })

  it('hides 7/9 that are stale AND unmanned via the -1 unknown sentinel', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 9, statusChangedAt: hoursAgo(STALE_HIDE_HOURS), man: -1 }),
        NOW,
      ),
    ).toBe(false)
  })

  it('keeps a stale but CREWED 7/9 fire (man > 0), however old the change', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 7, statusChangedAt: hoursAgo(999), man: 4 }),
        NOW,
      ),
    ).toBe(true)
  })

  it('keeps an unmanned 7/9 fire whose change is younger than 24h', () => {
    expect(
      isDisplayedOnMap(
        incident({ code: 7, statusChangedAt: hoursAgo(STALE_HIDE_HOURS - 1), man: 0 }),
        NOW,
      ),
    ).toBe(true)
  })

  it('uses occurredAt as the stale fallback when statusChangedAt is null', () => {
    expect(
      isDisplayedOnMap(
        incident({
          code: 9,
          statusChangedAt: null,
          occurredAt: hoursAgo(STALE_HIDE_HOURS),
          man: 0,
        }),
        NOW,
      ),
    ).toBe(false)
  })

  it('never applies the zombie rules to non-7/9 buckets', () => {
    // A done-bucket fire with demobilizedSince set and unmanned still follows
    // ONLY the 3h finished window — here it is fresh, so it shows.
    expect(
      isDisplayedOnMap(
        incident({
          code: 8,
          statusChangedAt: hoursAgo(1),
          man: 0,
          demobilizedSince: hoursAgo(48),
        }),
        NOW,
      ),
    ).toBe(true)
  })
})
