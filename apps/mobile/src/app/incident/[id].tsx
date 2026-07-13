import { Redirect, useLocalSearchParams } from 'expo-router'

/**
 * Deep-link target for `fogosportugal://incident/{id}` (plan 1.4). It carries no
 * UI of its own: it redirects to the map with the fire selected via the shared
 * `incident` search param, so the scheme link and the `fogosportugal.pt/?incident=…`
 * universal link converge on one selection mechanism. Declarative `<Redirect>`
 * means the cold-start path (app killed → link opens app) resolves on first mount
 * without imperative navigation.
 */
export default function IncidentDeepLink() {
  const { id } = useLocalSearchParams<{ id: string }>()

  // A malformed link with no id just lands on the map.
  if (!id) return <Redirect href="/" />

  return <Redirect href={{ pathname: '/', params: { incident: id } }} />
}
