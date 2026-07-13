import { View } from 'react-native'

import {
  Callout,
  Card,
  ContentScreen,
  Link,
  Paragraph,
  SourceRow,
} from '@/components/content-screen'

/**
 * Créditos e fontes — content ported verbatim from web
 * (apps/web/src/routes/creditos.tsx). External links open in the in-app browser
 * (expo-web-browser). Native header + back come from the root Stack.
 */
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

export default function CreditosScreen() {
  return (
    <ContentScreen lead="Este projeto assenta no trabalho de muitos — instituições públicas, comunidades e projetos que abriram os seus dados.">
      <Callout tone="accent" title="Fogos.pt e a VOST Portugal">
        <Paragraph>
          Um agradecimento especial e sentido ao{' '}
          <Link href="https://fogos.pt">Fogos.pt</Link> e à VOST Portugal, o
          projeto pioneiro que durante anos tornou a informação sobre incêndios
          acessível a todos. É sobre o seu trabalho, a sua visão e o seu código de
          base que este projeto se apoia. Sem eles, nada disto existiria — a eles a
          nossa gratidão.
        </Paragraph>
      </Callout>

      <Card title="Fontes de dados">
        {/* One View child so the Card's inter-child gap doesn't break the
            flush hairline dividers between rows. */}
        <View>
          {SOURCES.map((s, i) => (
            <SourceRow
              key={s.name}
              name={s.name}
              desc={s.desc}
              href={s.href}
              first={i === 0}
            />
          ))}
        </View>
      </Card>

      <Card>
        <Paragraph>
          Por fim, uma palavra de reconhecimento a quem está no terreno — os
          bombeiros, a proteção civil e todos os que arriscam para proteger
          pessoas, casas e floresta. Os dados são deles; o mérito também.
        </Paragraph>
      </Card>
    </ContentScreen>
  )
}
