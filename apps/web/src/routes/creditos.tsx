import { createFileRoute } from '@tanstack/react-router'
import { ExternalLink, Heart } from 'lucide-react'

import { ContentPage, InfoCard } from '#/components/content-page.tsx'
import { pageMeta } from '#/lib/seo.ts'

export const Route = createFileRoute('/creditos')({
  head: () =>
    pageMeta({
      title: 'Créditos e fontes — FogosPortugal',
      description:
        'Fontes de dados e créditos do FogosPortugal: Proteção Civil, IPMA e os serviços que tornam possível acompanhar os incêndios em Portugal.',
      path: '/creditos',
    }),
  component: Creditos,
})

const SOURCES = [
  {
    name: 'ANEPC / Proteção Civil',
    desc: 'Ocorrências e meios no terreno, a base da informação em tempo real.',
    href: 'https://prociv.gov.pt',
  },
  {
    name: 'ICNF',
    desc: 'Áreas ardidas e causas dos incêndios rurais.',
    href: 'https://www.icnf.pt',
  },
  {
    name: 'IPMA',
    desc: 'Meteorologia, avisos e risco de incêndio.',
    href: 'https://www.ipma.pt',
  },
  {
    name: 'NASA FIRMS',
    desc: 'Focos térmicos detetados por satélite.',
    href: 'https://firms.modaps.eosdis.nasa.gov',
  },
  {
    name: 'EFFIS — European Forest Fire Information System (Copernicus Emergency Management Service, © União Europeia)',
    desc: 'Índice meteorológico de perigo de incêndio (FWI).',
    href: 'https://forest-fire.emergency.copernicus.eu/',
  },
  {
    name: 'RainViewer',
    desc: 'Radar de precipitação.',
    href: 'https://www.rainviewer.com',
  },
  {
    name: 'Open-Meteo',
    desc: 'Dados de vento para a camada de partículas.',
    href: 'https://open-meteo.com',
  },
  {
    name: 'CARTO e OpenStreetMap',
    desc: 'Mapa base e cartografia.',
    href: 'https://www.openstreetmap.org/copyright',
  },
] as const

function Creditos() {
  return (
    <ContentPage
      title="Créditos e fontes"
      lead="Este projeto assenta no trabalho de muitos — instituições públicas, comunidades e projetos que abriram os seus dados."
    >
      <div className="rounded-2xl border border-orange-500/20 bg-orange-500/5 p-5 sm:p-6 dark:border-orange-400/20 dark:bg-orange-400/10">
        <h2 className="text-lg font-semibold text-foreground">
          Fogos.pt e a VOST Portugal
        </h2>
        <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
          Um agradecimento especial e sentido ao{' '}
          <a
            href="https://fogos.pt"
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-1 font-medium text-orange-700 hover:underline dark:text-orange-300"
          >
            Fogos.pt
            <ExternalLink className="size-3.5" aria-hidden />
          </a>{' '}
          e à VOST Portugal, o projeto pioneiro que durante anos tornou a
          informação sobre incêndios acessível a todos. É sobre o seu trabalho, a
          sua visão e o seu código de base que este projeto se apoia. Sem eles,
          nada disto existiria — a eles a nossa gratidão.
        </p>
      </div>

      <InfoCard title="Fontes de dados">
        <ul className="divide-y divide-border/60">
          {SOURCES.map((s) => (
            <li key={s.name} className="py-3 first:pt-0 last:pb-0">
              <a
                href={s.href}
                target="_blank"
                rel="noreferrer"
                className="group flex items-start justify-between gap-3"
              >
                <span>
                  <span className="font-medium text-foreground group-hover:underline">
                    {s.name}
                  </span>
                  <span className="mt-0.5 block text-sm text-muted-foreground">
                    {s.desc}
                  </span>
                </span>
                <ExternalLink
                  className="mt-0.5 size-4 shrink-0 text-muted-foreground/70"
                  aria-hidden
                />
              </a>
            </li>
          ))}
        </ul>
      </InfoCard>

      <div className="flex items-start gap-3 rounded-2xl border border-black/5 bg-white/70 p-5 shadow-sm backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/60 sm:p-6">
        <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-red-500/10 text-red-600 dark:text-red-400">
          <Heart className="size-5" aria-hidden />
        </span>
        <p className="text-sm leading-relaxed text-muted-foreground">
          Por fim, uma palavra de reconhecimento a quem está no terreno — os
          bombeiros, a proteção civil e todos os que arriscam para proteger
          pessoas, casas e floresta. Os dados são deles; o mérito também.
        </p>
      </div>
    </ContentPage>
  )
}
