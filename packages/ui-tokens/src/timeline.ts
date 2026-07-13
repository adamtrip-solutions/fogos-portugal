// Status-timeline builder — the single source of truth for the incident panel's
// "Evolução" list, including the synthetic "Alerta" origin entry. Pure, no
// React/RN imports. Ported from the client-side logic in the web incident panel
// (apps/web/src/components/incident-panel.tsx, EvolutionSection).

/** A real observed status transition. */
export interface StatusHistoryEntry {
  at: string
  code: number
  label: string
}

/** One row of the rendered timeline (newest → oldest). */
export interface StatusTimelineEntry {
  at: string
  /** Status code for the dot color; `-1` on the synthetic origin entry. */
  code: number
  label: string
  /**
   * True only for the synthetic "Alerta" origin entry, rendered from the
   * incident's `occurredAt` (not a stored observation). The panel styles it
   * muted rather than by status color.
   */
  synthetic: boolean
}

/** pt-PT label for the synthetic origin entry. */
const ALERT_LABEL = 'Alerta'

/**
 * Build the timeline rows for one incident, newest → oldest, appending the
 * synthetic "Alerta" origin at `occurredAt` as the last (oldest) row.
 *
 * The incident's start is not a stored observation: it is rendered client-side
 * as a muted first entry below the real ones. The origin is skipped when the
 * earliest real observation already sits at the exact same instant as
 * `occurredAt` (avoids a duplicate row).
 */
export function buildStatusTimeline(
  history: readonly StatusHistoryEntry[],
  occurredAt: string,
): StatusTimelineEntry[] {
  const ordered: StatusTimelineEntry[] = [...history]
    .sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
    .map((e) => ({ at: e.at, code: e.code, label: e.label, synthetic: false }))

  const earliest = ordered[ordered.length - 1]
  const showStart =
    !earliest || Date.parse(earliest.at) !== Date.parse(occurredAt)

  if (!showStart) return ordered
  return [
    ...ordered,
    { at: occurredAt, code: -1, label: ALERT_LABEL, synthetic: true },
  ]
}
