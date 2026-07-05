import { PageHeader } from '#/components/page-header.tsx'

/**
 * Shared scaffold for the static content pages (Sobre, Créditos, API). Mirrors
 * the statistics page: sticky toolbar, tinted background, a lead header and a
 * generous single column of glass cards.
 */
export function ContentPage({
  title,
  lead,
  children,
}: {
  title: string
  lead: string
  children: React.ReactNode
}) {
  return (
    <div className="min-h-[100dvh] bg-zinc-50 dark:bg-zinc-950">
      <PageHeader />
      <main className="mx-auto max-w-3xl px-4 py-8 sm:py-12">
        <header className="mb-8">
          <h1 className="text-2xl font-bold tracking-tight text-foreground sm:text-3xl">
            {title}
          </h1>
          <p className="mt-2 text-base leading-relaxed text-muted-foreground">
            {lead}
          </p>
        </header>
        <div className="space-y-5">{children}</div>
      </main>
    </div>
  )
}

/** A glass content card. `title` is optional so cards can be pure prose. */
export function InfoCard({
  title,
  children,
  className = '',
}: {
  title?: string
  children: React.ReactNode
  className?: string
}) {
  return (
    <section
      className={`rounded-2xl border border-black/5 bg-white/70 p-5 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60 sm:p-6 ${className}`}
    >
      {title && (
        <h2 className="mb-3 text-lg font-semibold text-foreground">{title}</h2>
      )}
      <div className="space-y-3 text-sm leading-relaxed text-muted-foreground">
        {children}
      </div>
    </section>
  )
}
