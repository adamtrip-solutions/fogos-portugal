import { createFileRoute } from '@tanstack/react-router'
import { Rss } from 'lucide-react'

import { ContentPage, InfoCard } from '#/components/content-page.tsx'

export const Route = createFileRoute('/api')({
  head: () => ({
    meta: [{ title: 'API pública — FogosPortugal' }],
  }),
  component: ApiPage,
})

function Endpoint({
  method,
  path,
  desc,
}: {
  method: string
  path: string
  desc: string
}) {
  return (
    <div className="flex flex-col gap-1 py-3 first:pt-0 last:pb-0 sm:flex-row sm:items-baseline sm:gap-3">
      <span className="inline-flex shrink-0 items-center rounded-md bg-zinc-900/5 px-1.5 py-0.5 font-mono text-[11px] font-semibold uppercase tracking-wide text-zinc-600 dark:bg-white/10 dark:text-zinc-300">
        {method}
      </span>
      <code className="break-all font-mono text-[13px] text-foreground">
        {path}
      </code>
      <span className="text-sm text-muted-foreground sm:ml-auto sm:text-right">
        {desc}
      </span>
    </div>
  )
}

function ApiPage() {
  return (
    <ContentPage
      title="API pública"
      lead="Os mesmos dados que alimentam este mapa vão ficar disponíveis para quem quiser construir sobre eles."
    >
      <InfoCard>
        <p>
          Acreditamos que a informação sobre incêndios deve circular. Por isso, a
          API que está por detrás deste site vai tornar-se pública — para
          investigadores, jornalistas, autarquias, programadores e qualquer
          pessoa que a queira usar para bem comum.
        </p>
      </InfoCard>

      <InfoCard title="Sem valor oficial">
        <p>
          Os dados são recolhidos e reprocessados automaticamente a partir de
          fontes públicas e fornecidos «tal como estão», sem garantias de
          exatidão, completude ou continuidade. Não constituem um registo
          oficial de ocorrências — se o seu caso de uso exige dados com valor
          oficial, consulte diretamente a ANEPC e as restantes entidades
          competentes.
        </p>
      </InfoCard>

      <InfoCard title="O que já existe">
        <p>
          Uma parte da infraestrutura já está a servir dados. O endpoint{' '}
          <span className="font-mono text-[13px] text-foreground">GraphQL</span>{' '}
          suporta o mapa e as estatísticas, e há exportações REST dos incêndios
          ativos em vários formatos:
        </p>
        <div className="divide-y divide-border/60 rounded-xl border border-border/60 bg-white/40 p-4 dark:bg-black/10">
          <Endpoint method="POST" path="/graphql" desc="Consulta principal" />
          <Endpoint
            method="GET"
            path="/v3/incidents/active.geojson"
            desc="Ativos em GeoJSON"
          />
          <Endpoint
            method="GET"
            path="/v3/incidents/active.csv"
            desc="Ativos em CSV"
          />
          <Endpoint
            method="GET"
            path="/v3/incidents/active.kml"
            desc="Ativos em KML"
          />
        </div>
      </InfoCard>

      <div className="rounded-2xl border border-orange-500/20 bg-orange-500/5 p-5 sm:p-6 dark:border-orange-400/20 dark:bg-orange-400/10">
        <div className="flex items-center gap-2">
          <Rss className="size-5 text-orange-600 dark:text-orange-400" aria-hidden />
          <h2 className="text-lg font-semibold text-foreground">
            Feeds — já utilizáveis hoje
          </h2>
        </div>
        <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
          Os feeds RSS / GeoRSS estão abertos e podem ser consumidos já, por
          agregadores ou por sistemas de alerta. Cada ocorrência localizada
          traz o seu ponto geográfico.
        </p>
        <div className="mt-3 divide-y divide-orange-500/15 rounded-xl border border-orange-500/20 bg-white/50 p-4 dark:border-orange-400/20 dark:bg-black/10">
          <Endpoint
            method="GET"
            path="/v3/feeds/incidents.rss"
            desc="Incêndios ativos e recentes"
          />
          <Endpoint
            method="GET"
            path="/v3/feeds/warnings.rss"
            desc="Avisos meteorológicos"
          />
        </div>
      </div>

      <InfoCard title="Chaves de acesso e documentação">
        <p>
          A documentação completa, os limites de utilização e as chaves de
          acesso estão a ser preparados. <span className="font-medium text-foreground">Em breve.</span>
        </p>
      </InfoCard>

      <InfoCard title="Utilização justa e atribuição">
        <p>
          Estes dados existem para servir o interesse público. Ao reutilizá-los,
          pedimos apenas bom senso: identifique a origem — FogosPortugal — e as
          fontes de dados originais (ANEPC, ICNF, IPMA e as restantes indicadas
          nos créditos). Evite sobrecarregar os serviços e dê preferência aos
          feeds e exportações em vez de recolhas agressivas.
        </p>
      </InfoCard>
    </ContentPage>
  )
}
