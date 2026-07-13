import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useQuery } from '@tanstack/react-query'

import type { SeasonStats } from '@fogos/api-client'
import {
  formatDuration,
  formatHectares,
  formatInteger,
  formatPercent,
} from '@fogos/ui-tokens'

import { StatTile } from '@/components/stats/stat-tile'
import { SectionCard } from '@/components/stats/section-card'
import {
  BurnAreaChart,
  CauseBreakdownChart,
  HourlyIgnitionsChart,
  IgnitionsYoYChart,
} from '@/components/stats/season-charts'
import type { ThemeColors } from '@/components/incident/section'
import { Spacing } from '@/constants/theme'
import { useTheme } from '@/hooks/use-theme'
import { currentSeasonYear, seasonStatsQueryOptions } from '@/hooks/use-season-stats'

const ACCENT = '#FF6E02'

export default function EstatisticasScreen() {
  const c = useTheme() as ThemeColors
  const insets = useSafeAreaInsets()
  // Derive the season year on every render (a cheap Intl call) so it rolls over
  // at the Lisbon-tz new year — this tab screen never unmounts to re-init a memo.
  const year = currentSeasonYear()

  const { data, isLoading, isError, refetch } = useQuery(
    seasonStatsQueryOptions(year),
  )

  return (
    <ScrollView
      style={{ backgroundColor: c.background }}
      contentContainerStyle={[
        styles.content,
        { paddingBottom: insets.bottom + Spacing.six },
      ]}
    >
      <View style={styles.intro}>
        <Text style={[styles.title, { color: c.text }]}>Época {year}</Text>
        <Text style={[styles.lead, { color: c.textSecondary }]}>
          Panorama nacional de ignições, área ardida, causas e tempos de resposta.
        </Text>
      </View>

      {isLoading && !data ? (
        <View style={styles.state}>
          <ActivityIndicator color={c.textSecondary} />
        </View>
      ) : isError && !data ? (
        <View style={styles.state}>
          <Text style={[styles.stateText, { color: c.textSecondary }]}>
            Não foi possível carregar as estatísticas. A tentar novamente…
          </Text>
          <Pressable onPress={() => refetch()} hitSlop={8}>
            <Text style={[styles.stateAction, { color: ACCENT }]}>
              Tentar novamente
            </Text>
          </Pressable>
        </View>
      ) : data ? (
        <Dashboard stats={data} c={c} />
      ) : null}
    </ScrollView>
  )
}

function Dashboard({ stats, c }: { stats: SeasonStats; c: ThemeColors }) {
  const { header } = stats
  return (
    <View style={styles.sections}>
      {/* Header tiles: 2-per-row + a full-width burn-area tile */}
      <View style={styles.tiles}>
        <StatTile
          label="Incêndios ativos"
          value={formatInteger(header.activeFires)}
          symbol="flame.fill"
          c={c}
        />
        <StatTile
          label="Ignições hoje"
          value={formatInteger(header.today)}
          symbol="waveform.path.ecg"
          c={c}
        />
        <StatTile
          label="Ignições ontem"
          value={formatInteger(header.yesterday)}
          symbol="calendar"
          c={c}
        />
        <StatTile
          label="Últimos 7 dias"
          value={formatInteger(header.week)}
          symbol="chart.line.uptrend.xyaxis"
          c={c}
        />
        <StatTile
          label="Área ardida no ano"
          value={
            header.burnAreaTotalHa != null
              ? formatHectares(header.burnAreaTotalHa)
              : '—'
          }
          symbol="flame.fill"
          full
          c={c}
        />
      </View>

      <SectionCard
        title="Ignições acumuladas"
        subtitle={`Total de fogos ao longo do ano, ${stats.year} vs ${stats.year - 1}.`}
        c={c}
      >
        <IgnitionsYoYChart
          current={stats.ignitionsCurrent}
          previous={stats.ignitionsPrevious}
          year={stats.year}
          c={c}
        />
      </SectionCard>

      <SectionCard
        title="Área ardida acumulada"
        subtitle="Hectares contabilizados pelo ICNF ao longo do ano."
        c={c}
      >
        <BurnAreaChart series={stats.burnAreaCumulative} c={c} />
      </SectionCard>

      <SectionCard
        title="Causas dos incêndios"
        subtitle="Distribuição por família de causa (ICNF), por número de ocorrências."
        c={c}
      >
        <CauseBreakdownChart causes={stats.causeBreakdown} c={c} />
      </SectionCard>

      <SectionCard
        title="Padrão horário das ignições"
        subtitle="Ignições por hora do dia (hoje)."
        c={c}
      >
        <HourlyIgnitionsChart hours={stats.ignitionsHourly} c={c} />
      </SectionCard>

      <SectionCard
        title="Falsos alarmes por distrito"
        subtitle="Percentagem de ocorrências classificadas como falso alarme ou alerta (mín. 20 ocorrências)."
        c={c}
      >
        <FalseAlarmTable stats={stats} c={c} />
      </SectionCard>

      <SectionCard
        title="Tempos de resposta"
        subtitle="Medianas nacionais das transições de estado das ocorrências no ano."
        c={c}
      >
        <ResponseTimes stats={stats} c={c} />
      </SectionCard>
    </View>
  )
}

function FalseAlarmTable({ stats, c }: { stats: SeasonStats; c: ThemeColors }) {
  if (stats.falseAlarmStats.length === 0) {
    return (
      <Text style={[styles.muted, { color: c.textSecondary }]}>
        Sem dados suficientes para o período.
      </Text>
    )
  }
  return (
    <View>
      <View style={[styles.tRow, styles.tHeadRow, { borderColor: c.backgroundSelected }]}>
        <Text style={[styles.th, styles.tDistrict, { color: c.textSecondary }]}>
          Distrito
        </Text>
        <Text style={[styles.th, styles.tNum, { color: c.textSecondary }]}>Total</Text>
        <Text style={[styles.th, styles.tNum, { color: c.textSecondary }]}>Falsos</Text>
        <Text style={[styles.th, styles.tNum, { color: c.textSecondary }]}>Taxa</Text>
      </View>
      {stats.falseAlarmStats.map((row) => (
        <View
          key={row.district}
          style={[styles.tRow, { borderColor: c.backgroundSelected }]}
        >
          <Text
            style={[styles.tDistrict, styles.tdStrong, { color: c.text }]}
            numberOfLines={1}
          >
            {row.district}
          </Text>
          <Text style={[styles.td, styles.tNum, { color: c.textSecondary }]}>
            {formatInteger(row.total)}
          </Text>
          <Text style={[styles.td, styles.tNum, { color: c.textSecondary }]}>
            {formatInteger(row.falseAlarms)}
          </Text>
          <Text style={[styles.tNum, styles.tdStrong, { color: c.text }]}>
            {formatPercent(row.rate)}
          </Text>
        </View>
      ))}
    </View>
  )
}

function ResponseTimes({ stats, c }: { stats: SeasonStats; c: ThemeColors }) {
  const rt = stats.responseTimeStats
  if (!rt || rt.count === 0) {
    return (
      <Text style={[styles.muted, { color: c.textSecondary }]}>
        Sem dados de tempos de resposta para o período.
      </Text>
    )
  }
  return (
    <View style={styles.tiles}>
      <StatTile
        label="Despacho → Chegada"
        value={
          rt.medianDispatchToArrivalSeconds != null
            ? formatDuration(rt.medianDispatchToArrivalSeconds)
            : '—'
        }
        hint="Mediana"
        c={c}
      />
      <StatTile
        label="Chegada → Resolução"
        value={
          rt.medianArrivalToControlSeconds != null
            ? formatDuration(rt.medianArrivalToControlSeconds)
            : '—'
        }
        hint="Mediana"
        c={c}
      />
      <StatTile
        label="Ocorrências analisadas"
        value={formatInteger(rt.count)}
        full
        c={c}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
  },
  intro: {
    marginBottom: Spacing.four,
    gap: Spacing.one,
  },
  title: {
    fontSize: 22,
    fontWeight: '700',
  },
  lead: {
    fontSize: 14,
    lineHeight: 20,
  },
  sections: {
    gap: Spacing.four,
  },
  tiles: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  state: {
    alignItems: 'center',
    gap: Spacing.three,
    paddingVertical: Spacing.six,
    paddingHorizontal: Spacing.four,
  },
  stateText: {
    fontSize: 14,
    textAlign: 'center',
    lineHeight: 20,
  },
  stateAction: {
    fontSize: 14,
    fontWeight: '600',
  },
  muted: {
    fontSize: 14,
    lineHeight: 20,
  },
  tRow: {
    flexDirection: 'row',
    alignItems: 'center',
    paddingVertical: Spacing.two,
    borderBottomWidth: StyleSheet.hairlineWidth,
  },
  tHeadRow: {
    paddingTop: 0,
  },
  th: {
    fontSize: 11,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.4,
  },
  td: {
    fontSize: 14,
  },
  tdStrong: {
    fontSize: 14,
    fontWeight: '600',
    fontVariant: ['tabular-nums'],
  },
  tDistrict: {
    flex: 1,
  },
  tNum: {
    width: 64,
    textAlign: 'right',
    fontVariant: ['tabular-nums'],
  },
})
