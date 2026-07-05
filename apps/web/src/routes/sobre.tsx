import { createFileRoute } from '@tanstack/react-router'
import { HeartHandshake, TriangleAlert } from 'lucide-react'

import { ContentPage, InfoCard } from '#/components/content-page.tsx'

export const Route = createFileRoute('/sobre')({
  head: () => ({
    meta: [{ title: 'Sobre o projeto — FogosPortugal' }],
  }),
  component: Sobre,
})

function Sobre() {
  return (
    <ContentPage
      title="Sobre o projeto"
      lead="Informação sobre incêndios rurais, rápida e clara, ao alcance de quem vive em Portugal."
    >
      <InfoCard title="Porque existe">
        <p>
          Todos os verões, Portugal convive com o fogo. Quando arde perto de
          casa, o que importa é uma coisa simples: saber o que se passa, sem
          demoras e sem ruído. É para isso que este projeto existe — reunir num
          único lugar a informação essencial sobre os incêndios em curso e
          torná-la fácil de consultar por qualquer pessoa.
        </p>
        <p>
          O mapa mostra as ocorrências ativas em tempo quase real, com o estado,
          os meios no terreno e o contexto meteorológico que ajuda a perceber o
          risco. Nada de registos, nada de barreiras: abre-se a página e a
          informação está lá.
        </p>
      </InfoCard>

      <div className="rounded-2xl border border-amber-500/25 bg-amber-500/5 p-5 sm:p-6 dark:border-amber-400/25 dark:bg-amber-400/10">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-amber-500/15 text-amber-700 dark:text-amber-300">
            <TriangleAlert className="size-5" aria-hidden />
          </span>
          <div className="space-y-2">
            <h2 className="text-lg font-semibold text-foreground">
              Isto não é uma fonte oficial
            </h2>
            <p className="text-sm leading-relaxed text-muted-foreground">
              O FogosPortugal é um projeto independente. A informação é
              recolhida automaticamente a partir de fontes públicas — ANEPC,
              ICNF, IPMA, entre outras — e pode chegar com atraso, estar
              incompleta ou conter erros. Não é um registo oficial de
              ocorrências e não deve ser a única base para decisões que
              envolvam a sua segurança.
            </p>
            <p className="text-sm leading-relaxed text-muted-foreground">
              Em emergência, ligue <span className="font-semibold text-foreground">112</span>.
              Siga sempre as indicações da Proteção Civil e das autoridades
              locais — são elas a fonte que conta.
            </p>
          </div>
        </div>
      </div>

      <InfoCard title="Para quem">
        <p>
          Para quem tem familiares numa zona ameaçada, para quem vai viajar,
          para quem trabalha na proteção civil ou no jornalismo, e para todos os
          que simplesmente querem estar informados durante a época de incêndios.
          A prioridade é a clareza: informação útil, apresentada com sobriedade.
        </p>
      </InfoCard>

      <div className="rounded-2xl border border-emerald-500/20 bg-emerald-500/5 p-5 sm:p-6 dark:border-emerald-400/20 dark:bg-emerald-400/10">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-emerald-500/15 text-emerald-700 dark:text-emerald-300">
            <HeartHandshake className="size-5" aria-hidden />
          </span>
          <div className="space-y-2">
            <h2 className="text-lg font-semibold text-foreground">
              O nosso compromisso
            </h2>
            <p className="text-sm leading-relaxed text-muted-foreground">
              Este projeto não tem publicidade, não tem subscrições pagas e não
              tem fins lucrativos — e nunca terá. Ninguém ganha dinheiro com o
              acesso a esta informação. O código será aberto e disponibilizado
              publicamente, para que qualquer pessoa o possa consultar, verificar
              ou reutilizar.
            </p>
            <p className="text-sm leading-relaxed text-muted-foreground">
              É um trabalho de natureza cívica, feito por quem se interessa pelo
              tema, ao serviço da comunidade.
            </p>
          </div>
        </div>
      </div>
    </ContentPage>
  )
}
