import { Moon, Sun } from 'lucide-react'

import { toggleTheme, useTheme } from '#/lib/theme.ts'

/**
 * Theme switch. Defaults to a standalone floating-glass style; pass `className`
 * to override (e.g. a ghost button that sits inside the toolbar pill).
 */
export function ThemeToggle({ className }: { className?: string }) {
  const theme = useTheme()

  return (
    <button
      type="button"
      onClick={toggleTheme}
      aria-label="Alternar tema"
      className={
        className ??
        'flex size-10 items-center justify-center rounded-full border border-black/5 bg-white/75 text-zinc-700 shadow-lg backdrop-blur-xl transition-colors hover:bg-white/90 dark:border-white/10 dark:bg-zinc-900/70 dark:text-zinc-200 dark:hover:bg-zinc-900/90'
      }
    >
      {theme === 'dark' ? (
        <Sun className="size-[18px]" />
      ) : (
        <Moon className="size-[18px]" />
      )}
    </button>
  )
}
