import { Link } from '@tanstack/react-router'
import { Dialog as DialogPrimitive } from 'radix-ui'
import {
  BookOpen,
  ChartColumn,
  Code,
  Flame,
  Info,
  Map,
  X,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

import { Separator } from '#/components/ui/separator.tsx'

/** Release version injected at build time from package.json (vite define). */
const APP_VERSION = `v${__APP_VERSION__}`

const NAV_LINKS = [
  { to: '/', label: 'Mapa', Icon: Map, exact: true },
  { to: '/estatisticas', label: 'Estatísticas', Icon: ChartColumn, exact: false },
] as const

const PAGE_LINKS = [
  { to: '/sobre', label: 'Sobre o projeto', Icon: Info },
  { to: '/creditos', label: 'Créditos e fontes', Icon: BookOpen },
  { to: '/api', label: 'API pública', Icon: Code },
] as const

// TanStack Router concatenates `className` with `activeProps.className`, so the
// shared layout lives in BASE and the color variants are mutually exclusive via
// active/inactiveProps — never both on the element, so no Tailwind conflicts.
const ITEM_BASE =
  'flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors'
const ITEM_INACTIVE =
  'text-zinc-700 hover:bg-black/5 dark:text-zinc-200 dark:hover:bg-white/10'
const ITEM_ACTIVE =
  'bg-zinc-900 text-white shadow-sm dark:bg-white dark:text-zinc-900'

interface AppDrawerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

/**
 * Left slide-in navigation drawer (radix Dialog). Reachable from the toolbar
 * hamburger on every route. Holds primary nav, the content pages, and a fixed
 * civic footer. Escape and overlay click close it.
 */
export function AppDrawer({ open, onOpenChange }: AppDrawerProps) {
  const close = () => onOpenChange(false)

  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/40 backdrop-blur-sm data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:animate-in data-[state=open]:fade-in-0" />
        <DialogPrimitive.Content className="fixed inset-y-0 left-0 z-50 flex w-[320px] max-w-[85vw] flex-col border-r border-black/10 bg-white/90 shadow-2xl backdrop-blur-xl duration-300 data-[state=closed]:animate-out data-[state=closed]:slide-out-to-left data-[state=open]:animate-in data-[state=open]:slide-in-from-left dark:border-white/10 dark:bg-zinc-900/90">
          {/* Header */}
          <div className="flex items-start justify-between gap-2 p-5">
            <div className="flex items-start gap-3">
              <span className="flex size-9 items-center justify-center rounded-lg bg-gradient-to-br from-orange-500 to-red-600 shadow-sm">
                <Flame className="size-5 text-white" aria-hidden />
              </span>
              <div>
                <DialogPrimitive.Title className="text-base font-semibold leading-tight text-zinc-900 dark:text-zinc-50">
                  FogosPortugal
                </DialogPrimitive.Title>
                <DialogPrimitive.Description className="mt-0.5 text-xs leading-snug text-muted-foreground">
                  Acompanhamento de incêndios em Portugal
                </DialogPrimitive.Description>
              </div>
            </div>
            <DialogPrimitive.Close
              aria-label="Fechar menu"
              className="flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
            >
              <X className="size-4" aria-hidden />
            </DialogPrimitive.Close>
          </div>

          <div className="flex-1 overflow-y-auto px-3 pb-3">
            {/* Primary nav */}
            <nav className="space-y-1">
              {NAV_LINKS.map(({ to, label, Icon, exact }) => (
                <DrawerLink
                  key={to}
                  to={to}
                  label={label}
                  Icon={Icon}
                  exact={exact}
                  onNavigate={close}
                />
              ))}
            </nav>

            <Separator className="my-3 bg-black/10 dark:bg-white/10" />

            {/* Content pages */}
            <nav className="space-y-1">
              {PAGE_LINKS.map(({ to, label, Icon }) => (
                <DrawerLink
                  key={to}
                  to={to}
                  label={label}
                  Icon={Icon}
                  exact={false}
                  onNavigate={close}
                />
              ))}
            </nav>
          </div>

          {/* Footer — always visible */}
          <div className="border-t border-black/10 p-5 dark:border-white/10">
            <p className="text-sm font-semibold text-zinc-900 dark:text-zinc-50">
              Sem publicidade. Sem fins lucrativos.
            </p>
            <p className="mt-1 text-xs leading-snug text-muted-foreground">
              Código aberto — em breve no GitHub.
            </p>
            <p className="mt-2 text-xs leading-snug text-muted-foreground">
              Fonte não oficial — em emergência, ligue{' '}
              <span className="font-semibold text-zinc-900 dark:text-zinc-50">112</span>.
            </p>
            <p className="mt-3 text-[11px] tabular-nums text-muted-foreground/70">
              {APP_VERSION}
            </p>
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}

function DrawerLink({
  to,
  label,
  Icon,
  exact,
  onNavigate,
}: {
  to: string
  label: string
  Icon: LucideIcon
  exact: boolean
  onNavigate: () => void
}) {
  return (
    <Link
      to={to}
      activeOptions={{ exact }}
      onClick={onNavigate}
      className={ITEM_BASE}
      activeProps={{ className: ITEM_ACTIVE }}
      inactiveProps={{ className: ITEM_INACTIVE }}
    >
      <Icon className="size-4 shrink-0" aria-hidden />
      {label}
    </Link>
  )
}
