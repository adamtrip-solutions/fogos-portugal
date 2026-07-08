// Hand-written types mirroring the HotChocolate GraphQL schema — ported from
// apps/web/src/lib/fogos/types.ts (list-item subset only for the map). Field
// names are camelCase; `kind` serializes as a GraphQL enum name.
//
// COPY, not import: the web app's types live behind server-fn wrappers we don't
// want on native. Extracting a shared packages/fogos-client is a deliberate
// later refactor (see the mobile plan) — do not pre-abstract.

export type IncidentKind =
  | 'FIRE'
  | 'URBAN_FIRE'
  | 'TRANSPORT_FIRE'
  | 'OTHER_FIRE'
  | 'FMA'
  | 'OTHER'
  | (string & {})

export interface Coordinates {
  latitude: number
  longitude: number
}

export interface IncidentStatus {
  /** Status codes 3-6 are considered active. */
  code: number
  label: string
  /** Hex without a leading `#`, e.g. "B81E1F". */
  color: string
}

/** Escalation / rekindle / critical-conditions flags queried on list items. */
export interface IncidentSignalsLite {
  escalating: boolean
  rekindle: boolean
  criticalConditions: boolean
}

/** Shape returned by the `activeIncidents` list query. */
export interface IncidentListItem {
  id: string
  location: string
  district: string | null
  concelho: string | null
  freguesia: string | null
  coordinates: Coordinates | null
  status: IncidentStatus
  kind: IncidentKind
  natureza: string | null
  important: boolean
  occurredAt: string
  updatedAt: string
  /** When the status last changed; null when no transition was ever recorded. */
  statusChangedAt: string | null
  resources: {
    man: number
    terrain: number
    aerial: number
    aquatic: number
  }
  /** Flags for badges and the map pulse/halo. */
  signals: IncidentSignalsLite
}
