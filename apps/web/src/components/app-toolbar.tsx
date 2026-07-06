import { useState } from 'react'
import { Link } from '@tanstack/react-router'
import { ChartColumn, Flame, List, Map, Menu } from 'lucide-react'

import { countLabel } from '#/lib/fogos/format.ts'
import { AppDrawer } from '#/components/app-drawer.tsx'
import { ThemeToggle } from '#/components/theme-toggle.tsx'

// ── Shared glass + item styling ──────────────────────────────────────────────

const PILL =
  'flex items-center gap-1 rounded-2xl border border-black/5 bg-white/75 p-1 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70'

const GHOST_ITEM =
  'flex h-9 items-center gap-1.5 rounded-xl px-3 text-sm font-medium text-zinc-600 transition-colors hover:bg-black/5 dark:text-zinc-300 dark:hover:bg-white/10'

const ICON_ITEM =
  'flex size-9 items-center justify-center rounded-xl text-zinc-600 transition-colors hover:bg-black/5 dark:text-zinc-300 dark:hover:bg-white/10'

// Split base/variant because TanStack Router concatenates `className` with
// `activeProps.className` — overlapping color utilities would conflict.
const SEG_BASE =
  'flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-sm font-medium transition-colors'
const SEG_INACTIVE =
  'text-zinc-600 hover:bg-black/5 dark:text-zinc-300 dark:hover:bg-white/10'
const SEG_ACTIVE =
  'bg-zinc-900 text-white shadow-sm dark:bg-white dark:text-zinc-900 [view-transition-name:nav-active]'

function Divider({ className = '' }: { className?: string }) {
  return (
    <span
      aria-hidden
      className={`mx-0.5 h-5 w-px bg-black/10 dark:bg-white/15 ${className}`}
    />
  )
}

const NAV_LINKS = [
  { to: '/', label: 'Mapa', Icon: Map, exact: true },
  { to: '/ocorrencias', label: 'Ocorrências', Icon: List, exact: false },
  { to: '/estatisticas', label: 'Estatísticas', Icon: ChartColumn, exact: false },
] as const

// ── Pills ────────────────────────────────────────────────────────────────────

function LeftPill({
  count,
  isLoading,
  activeOnly,
  onOpenDrawer,
}: {
  count?: number
  isLoading?: boolean
  activeOnly?: boolean
  onOpenDrawer: () => void
}) {
  const showCount = count !== undefined && count > 0 && !isLoading

  return (
    <div className={PILL}>
      <button
        type="button"
        aria-label="Abrir menu"
        onClick={onOpenDrawer}
        className={ICON_ITEM}
      >
        <Menu className="size-[18px]" aria-hidden />
      </button>
      <Link
        to="/"
        viewTransition
        className="flex items-center gap-2.5 rounded-xl pl-1 pr-1.5 md:pr-2.5"
      >
        <span className="flex size-8 items-center justify-center rounded-lg bg-gradient-to-br from-orange-500 to-red-600 shadow-sm">
          <Flame className="size-[18px] text-white" aria-hidden />
        </span>
        <span className="hidden font-semibold text-zinc-900 md:inline dark:text-zinc-50">
          FogosPortugal
        </span>
      </Link>
      {showCount && (
        <div className="hidden items-center gap-1 md:flex">
          <Divider />
          <span className="pr-2 pl-1 text-sm font-medium tabular-nums text-zinc-600 dark:text-zinc-300">
            {countLabel(count, activeOnly)}
          </span>
        </div>
      )}
    </div>
  )
}

function NavSegment() {
  return (
    <div className="hidden items-center gap-1 md:flex [view-transition-name:app-nav]">
      {NAV_LINKS.map(({ to, label, Icon, exact }) => (
        <Link
          key={to}
          to={to}
          viewTransition
          activeOptions={{ exact }}
          className={SEG_BASE}
          activeProps={{ className: SEG_ACTIVE }}
          inactiveProps={{ className: SEG_INACTIVE }}
        >
          <Icon className="size-4" aria-hidden />
          {label}
        </Link>
      ))}
    </div>
  )
}

function ActiveOnlyToggle({
  activeOnly,
  onChange,
}: {
  activeOnly: boolean
  onChange: (v: boolean) => void
}) {
  return (
    <button
      type="button"
      aria-pressed={activeOnly}
      aria-label="Só ativos"
      title="Mostrar só incêndios ativos"
      onClick={() => onChange(!activeOnly)}
      className={
        activeOnly
          ? 'flex h-9 items-center gap-2 rounded-xl bg-zinc-900 px-2.5 text-sm font-medium text-white transition-colors md:px-3 dark:bg-white dark:text-zinc-900'
          : `${GHOST_ITEM} px-2.5 md:px-3`
      }
    >
      <span
        aria-hidden
        className="size-2 rounded-full"
        style={{ backgroundColor: '#B81E1F' }}
      />
      <span className="hidden md:inline">Só ativos</span>
    </button>
  )
}

function RightPill({
  showActiveOnly,
  activeOnly = false,
  onActiveOnlyChange,
}: {
  showActiveOnly?: boolean
  activeOnly?: boolean
  onActiveOnlyChange?: (v: boolean) => void
}) {
  const withActive = showActiveOnly && onActiveOnlyChange
  return (
    <div className={PILL}>
      <NavSegment />
      {withActive && (
        <>
          {/* Só ativos is always visible (even mobile), so the nav divider only
              shows on md+ where the segment appears. */}
          <Divider className="hidden md:block" />
          <ActiveOnlyToggle
            activeOnly={activeOnly}
            onChange={onActiveOnlyChange!}
          />
        </>
      )}
      {/* Divider before the theme toggle: when Só ativos is present it always has a
          neighbour to its left; otherwise (bar variant) only the md+ nav does. */}
      <Divider className={withActive ? '' : 'hidden md:block'} />
      <ThemeToggle className={ICON_ITEM} />
    </div>
  )
}

// ── Toolbar ──────────────────────────────────────────────────────────────────

interface AppToolbarProps {
  /**
   * `map` places the two pills at the top corners over the fullscreen map;
   * `bar` renders a sticky top bar for the content pages.
   */
  variant: 'map' | 'bar'
  count?: number
  isLoading?: boolean
  activeOnly?: boolean
  onActiveOnlyChange?: (v: boolean) => void
  /** Whether to show the "Só ativos" toggle (map route only). */
  showActiveOnly?: boolean
  /** Extra content stacked under the right pill (e.g. weather controls). */
  rightSlot?: React.ReactNode
  /** Fade + disable the right cluster (map: when an incident panel is open). */
  rightHidden?: boolean
}

/**
 * Consolidated top navigation: a left "brand + menu" pill and a right
 * "nav + actions" pill, plus the slide-in drawer they open. Shared by the map
 * route (`variant="map"`, absolute corners) and content pages (`variant="bar"`,
 * a sticky header via PageHeader).
 */
export function AppToolbar({
  variant,
  count,
  isLoading,
  activeOnly,
  onActiveOnlyChange,
  showActiveOnly,
  rightSlot,
  rightHidden,
}: AppToolbarProps) {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const openDrawer = () => setDrawerOpen(true)

  const left = (
    <LeftPill
      count={count}
      isLoading={isLoading}
      activeOnly={activeOnly}
      onOpenDrawer={openDrawer}
    />
  )
  const right = (
    <RightPill
      showActiveOnly={showActiveOnly}
      activeOnly={activeOnly}
      onActiveOnlyChange={onActiveOnlyChange}
    />
  )

  const drawer = <AppDrawer open={drawerOpen} onOpenChange={setDrawerOpen} />

  if (variant === 'bar') {
    return (
      <header className="sticky top-0 z-30 border-b border-black/5 bg-white/75 backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 px-4 py-3">
          {left}
          {right}
        </div>
        {drawer}
      </header>
    )
  }

  return (
    <>
      <div className="pointer-events-auto absolute left-4 top-4">{left}</div>
      <div
        className={`absolute right-4 top-4 flex flex-col items-end gap-3 transition-opacity duration-200 ${
          rightHidden
            ? 'pointer-events-none opacity-0'
            : 'pointer-events-auto opacity-100'
        }`}
      >
        {right}
        {rightSlot}
      </div>
      {drawer}
    </>
  )
}
