import { Link } from '@tanstack/react-router'
import { ChevronRight } from 'lucide-react'

import {
  formatRelative,
  incidentTitle,
  locationParts,
  statusColorForCode,
} from '#/lib/fogos/format.ts'
import type { IncidentListItem } from '#/lib/fogos/types.ts'

/**
 * Canonical incident list row: a card link to the map with the incident
 * preselected. Shared by the concelho profile and the incidents table's mobile
 * layout so both stay in visual lockstep.
 */
export function IncidentRow({ incident }: { incident: IncidentListItem }) {
  const place = locationParts(
    incident.freguesia,
    incident.concelho,
    incident.district,
  )
  return (
    <li>
      <Link
        to="/"
        search={{ incident: incident.id }}
        viewTransition
        className="flex items-center gap-3 rounded-2xl border border-black/5 bg-white/70 px-4 py-3 shadow-sm transition-colors hover:bg-white/90 dark:border-white/10 dark:bg-zinc-900/60 dark:hover:bg-zinc-900/80"
      >
        <span
          className="mt-0.5 size-2.5 shrink-0 rounded-full"
          style={{ backgroundColor: statusColorForCode(incident.status.code) }}
        />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold text-foreground">
            {incidentTitle(incident)}
          </p>
          <p className="truncate text-xs text-muted-foreground">
            {place || incident.location}
          </p>
          <p className="text-xs text-muted-foreground">
            {incident.status.label}
            <span className="mx-1.5 opacity-40">·</span>
            {formatRelative(incident.occurredAt)}
          </p>
        </div>
        <ChevronRight
          className="size-4 shrink-0 text-muted-foreground"
          aria-hidden
        />
      </Link>
    </li>
  )
}
