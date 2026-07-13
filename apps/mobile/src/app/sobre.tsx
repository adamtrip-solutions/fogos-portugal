import { Callout, Card, ContentScreen, Paragraph, Strong } from '@/components/content-screen'

/**
 * Sobre o projeto — content ported verbatim from web (apps/web/src/routes/sobre.tsx).
 * The "não é uma fonte oficial / ligue 112" disclaimer and the civic-commitment
 * note double as the store-review-required about page. Native header + back come
 * from the root Stack screen options.
 */
export default function SobreScreen() {
  return (
    <ContentScreen lead="Informação sobre incêndios rurais, rápida e clara, ao alcance de quem vive em Portugal.">
      <Card title="Porque existe">
        <Paragraph>
          Todos os verões, Portugal convive com o fogo. Quando arde perto de casa,
          o que importa é uma coisa simples: saber o que se passa, sem demoras e
          sem ruído. É para isso que este projeto existe — reunir num único lugar a
          informação essencial sobre os incêndios em curso e torná-la fácil de
          consultar por qualquer pessoa.
        </Paragraph>
        <Paragraph>
          O mapa mostra as ocorrências ativas em tempo quase real, com o estado, os
          meios no terreno e o contexto meteorológico que ajuda a perceber o risco.
          Nada de registos, nada de barreiras: abre-se a página e a informação está
          lá.
        </Paragraph>
      </Card>

      <Callout tone="warn" title="Isto não é uma fonte oficial">
        <Paragraph>
          O FogosPortugal é um projeto independente. A informação é recolhida
          automaticamente a partir de fontes públicas — ANEPC, ICNF, IPMA, entre
          outras — e pode chegar com atraso, estar incompleta ou conter erros. Não
          é um registo oficial de ocorrências e não deve ser a única base para
          decisões que envolvam a sua segurança.
        </Paragraph>
        <Paragraph>
          Em emergência, ligue <Strong>112</Strong>. Siga sempre as indicações da
          Proteção Civil e das autoridades locais — são elas a fonte que conta.
        </Paragraph>
      </Callout>

      <Card title="Para quem">
        <Paragraph>
          Para quem tem familiares numa zona ameaçada, para quem vai viajar, para
          quem trabalha na proteção civil ou no jornalismo, e para todos os que
          simplesmente querem estar informados durante a época de incêndios. A
          prioridade é a clareza: informação útil, apresentada com sobriedade.
        </Paragraph>
      </Card>

      <Callout tone="positive" title="O nosso compromisso">
        <Paragraph>
          Este projeto não tem publicidade, não tem subscrições pagas e não tem
          fins lucrativos — e nunca terá. Ninguém ganha dinheiro com o acesso a
          esta informação. O código será aberto e disponibilizado publicamente,
          para que qualquer pessoa o possa consultar, verificar ou reutilizar.
        </Paragraph>
        <Paragraph>
          É um trabalho de natureza cívica, feito por quem se interessa pelo tema,
          ao serviço da comunidade.
        </Paragraph>
      </Callout>
    </ContentScreen>
  )
}
