import { AppToolbar } from '#/components/app-toolbar.tsx'

/**
 * Sticky top bar for the non-map pages (statistics, concelho, content pages).
 * A thin wrapper over the shared toolbar in its `bar` layout so the hamburger,
 * drawer and nav stay consistent everywhere.
 */
export function PageHeader() {
  return <AppToolbar variant="bar" />
}
