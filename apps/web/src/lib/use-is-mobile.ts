import { useEffect, useState } from 'react'

/**
 * Tracks the `(max-width: 767px)` breakpoint. Starts `false` so SSR and the
 * first client render agree (no hydration mismatch); the effect corrects it.
 */
export function useIsMobile(): boolean {
  const [isMobile, setIsMobile] = useState(false)
  useEffect(() => {
    const mql = window.matchMedia('(max-width: 767px)')
    const update = () => setIsMobile(mql.matches)
    update()
    mql.addEventListener('change', update)
    return () => mql.removeEventListener('change', update)
  }, [])
  return isMobile
}
