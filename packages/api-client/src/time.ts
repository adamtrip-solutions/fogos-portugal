// Lisbon-time cutoff helper shared by the incident feeds. Ported verbatim from
// the web app (apps/web/src/lib/fogos/api.ts) so mobile and web fetch the exact
// same window. Uses `Intl.DateTimeFormat` with a `timeZone` — Hermes-safe
// (modern Hermes ships Intl; no RelativeTimeFormat is involved here). The
// `en-CA` locale renders the calendar date as `YYYY-MM-DD`.

/** Lisbon calendar date (YYYY-MM-DD) for a given instant. */
const lisbonDateFmt = new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Europe/Lisbon',
})

/**
 * Lisbon calendar day (YYYY-MM-DD) `days` before now — the `updatedAfter`/`after`
 * cutoff the incident feeds pass to the API. `days` counts back in whole 24h
 * steps from the current instant, then snaps to that instant's Lisbon calendar
 * day (so DST shifts never move the boundary by more than a day).
 */
export function lisbonDateDaysAgo(days: number): string {
  return lisbonDateFmt.format(new Date(Date.now() - days * 24 * 60 * 60 * 1000))
}
