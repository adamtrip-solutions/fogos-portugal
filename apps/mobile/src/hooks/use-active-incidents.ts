import { useCallback, useEffect, useRef, useState } from 'react'

import { fetchActiveIncidents } from '@/lib/fogos/api'
import type { IncidentListItem } from '@/lib/fogos/types'

const POLL_INTERVAL_MS = 60_000

export interface ActiveIncidentsState {
  incidents: IncidentListItem[]
  /** True only until the first successful (or failed) load resolves. */
  loading: boolean
  error: string | null
  refetch: () => void
}

/**
 * Polls the live `activeIncidents` feed every 60s (mirrors the web map). Plain
 * setInterval + fetch — the default Expo template carries no TanStack Query and
 * this phase deliberately avoids adding heavy deps. Poll refetches are silent:
 * `loading` flips false after the first load and never returns, so the map never
 * flashes a spinner on a background tick. An in-flight request is aborted when a
 * new one starts or the screen unmounts.
 */
export function useActiveIncidents(): ActiveIncidentsState {
  const [incidents, setIncidents] = useState<IncidentListItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  const load = useCallback(async () => {
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    try {
      const data = await fetchActiveIncidents(controller.signal)
      if (controller.signal.aborted) return
      setIncidents(data)
      setError(null)
    } catch (err) {
      if (controller.signal.aborted) return
      setError(err instanceof Error ? err.message : 'Erro ao carregar incêndios')
    } finally {
      if (!controller.signal.aborted) setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
    const timer = setInterval(() => void load(), POLL_INTERVAL_MS)
    return () => {
      clearInterval(timer)
      abortRef.current?.abort()
    }
  }, [load])

  return { incidents, loading, error, refetch: () => void load() }
}
