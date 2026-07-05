// Client-side alert subscription state + pure event helpers. The subscription id
// lives in localStorage (anonymous, one per device); event de-duplication and
// formatting are pure so they can be unit-tested without a DOM.

import type { AlertEvent } from './types.ts'

const STORAGE_KEY = 'fogos.alertSubscriptionId'
const SEEN_KEY = 'fogos.alertSeenAt'

/** The persisted subscription id, or null. Safe on the server / private mode. */
export function readSubscriptionId(): string | null {
  if (typeof localStorage === 'undefined') return null
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}

export function writeSubscriptionId(id: string | null): void {
  if (typeof localStorage === 'undefined') return
  try {
    if (id) localStorage.setItem(STORAGE_KEY, id)
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    // ignore storage failures
  }
}

/** ISO timestamp of the newest event already surfaced to the user (dedupe cursor). */
export function readSeenAt(): string | null {
  if (typeof localStorage === 'undefined') return null
  try {
    return localStorage.getItem(SEEN_KEY)
  } catch {
    return null
  }
}

export function writeSeenAt(iso: string): void {
  if (typeof localStorage === 'undefined') return
  try {
    localStorage.setItem(SEEN_KEY, iso)
  } catch {
    // ignore
  }
}

/**
 * Given the polled events (newest first, as the API returns them) and the
 * timestamp of the last event already shown, returns only the genuinely new
 * ones, oldest→newest, each unique by id. `seenAt` null ⇒ everything is new.
 */
export function newEvents(
  events: readonly AlertEvent[],
  seenAt: string | null,
): AlertEvent[] {
  const cutoff = seenAt ? Date.parse(seenAt) : Number.NEGATIVE_INFINITY
  const seenIds = new Set<string>()
  const fresh: AlertEvent[] = []
  for (const e of events) {
    if (seenIds.has(e.id)) continue
    seenIds.add(e.id)
    if (Date.parse(e.createdAt) > cutoff) fresh.push(e)
  }
  return fresh.sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt))
}

/** The newest createdAt across the events, or null when empty. */
export function latestCreatedAt(events: readonly AlertEvent[]): string | null {
  let max: number | null = null
  let iso: string | null = null
  for (const e of events) {
    const t = Date.parse(e.createdAt)
    if (max == null || t > max) {
      max = t
      iso = e.createdAt
    }
  }
  return iso
}

/** PT title for the toast/notification, keyed by the event kind. */
export const ALERT_KIND_TITLE: Record<string, string> = {
  NEW_INCIDENT: 'Novo incêndio',
  ESCALATION: 'Ocorrência em escalada',
  REKINDLE: 'Reacendimento',
  RISK: 'Risco de incêndio',
}

export function alertKindTitle(kind: string): string {
  return ALERT_KIND_TITLE[kind] ?? 'Alerta'
}
