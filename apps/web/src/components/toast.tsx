import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
} from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { Flame, X } from 'lucide-react'

// Minimal dependency-free toast system (no shadcn sonner). A provider mounts a
// fixed stack; `useToast()` pushes transient cards that auto-dismiss.

export interface ToastInput {
  title: string
  description?: string
  /** Optional click target (internal path). */
  href?: string
  /** Auto-dismiss delay in ms (default 8000; 0 = sticky). */
  durationMs?: number
}

interface ToastItem extends ToastInput {
  id: number
}

interface ToastContextValue {
  toast: (input: ToastInput) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used within <ToastProvider>')
  return ctx
}

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const nextId = useRef(1)

  const dismiss = useCallback((id: number) => {
    setItems((prev) => prev.filter((t) => t.id !== id))
  }, [])

  const toast = useCallback(
    (input: ToastInput) => {
      const id = nextId.current++
      setItems((prev) => [...prev, { ...input, id }])
      const duration = input.durationMs ?? 8000
      if (duration > 0) {
        setTimeout(() => dismiss(id), duration)
      }
    },
    [dismiss],
  )

  const value = useMemo(() => ({ toast }), [toast])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-[calc(100vw-2rem)] max-w-sm flex-col gap-2">
        <AnimatePresence initial={false}>
          {items.map((item) => (
            <ToastCard key={item.id} item={item} onDismiss={() => dismiss(item.id)} />
          ))}
        </AnimatePresence>
      </div>
    </ToastContext.Provider>
  )
}

function ToastCard({
  item,
  onDismiss,
}: {
  item: ToastItem
  onDismiss: () => void
}) {
  const body = (
    <>
      <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-gradient-to-br from-orange-500 to-red-600 shadow-sm">
        <Flame className="size-4 text-white" aria-hidden />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-semibold text-zinc-900 dark:text-zinc-50">
          {item.title}
        </p>
        {item.description && (
          <p className="mt-0.5 text-sm text-zinc-600 dark:text-zinc-300">
            {item.description}
          </p>
        )}
      </div>
    </>
  )

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: 16, scale: 0.96 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, x: 24, scale: 0.96 }}
      transition={{ type: 'spring', stiffness: 320, damping: 30 }}
      className="pointer-events-auto flex items-start gap-3 rounded-2xl border border-black/5 bg-white/90 p-3 pr-2 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/85"
    >
      {item.href ? (
        <a href={item.href} className="flex min-w-0 flex-1 items-start gap-3">
          {body}
        </a>
      ) : (
        <div className="flex min-w-0 flex-1 items-start gap-3">{body}</div>
      )}
      <button
        type="button"
        onClick={onDismiss}
        aria-label="Dispensar"
        className="flex size-6 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-black/5 dark:hover:bg-white/10"
      >
        <X className="size-3.5" />
      </button>
    </motion.div>
  )
}
