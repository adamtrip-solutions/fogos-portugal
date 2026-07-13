import { useCallback, useState } from 'react'
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useRouter } from 'expo-router'
import { SymbolView } from 'expo-symbols'
import type { SFSymbol } from 'expo-symbols'
import { useQuery } from '@tanstack/react-query'

import { situationDeltas } from '@fogos/api-client'
import type {
  IncidentListItem,
  SituationDelta,
  SituationMetric,
  SituationReport,
} from '@fogos/api-client'
import {
  formatClock,
  formatDayMonth,
  formatInteger,
  formatSignedInteger,
  incidentTitle,
  locationParts,
  situationSlotLabel,
  statusColorForCode,
} from '@fogos/ui-tokens'

import { StatTile } from '@/components/stats/stat-tile'
import type { ThemeColors } from '@/components/incident/section'
import { Spacing } from '@/constants/theme'
import { useTheme } from '@/hooks/use-theme'
import { recentIncidentsQueryOptions } from '@/hooks/use-recent-incidents'
import { situationReportsQueryOptions } from '@/hooks/use-situation-reports'

const ACCENT = '#FF6E02'

// Headline tiles — order + labels mirror the mobile stats convention
// ("Incêndios ativos") and web's situation hero (Operacionais / Meios …).
const STAT_TILES: {
  metric: SituationMetric
  label: string
  symbol: SFSymbol
  glyph: string
}[] = [
  { metric: 'activeFires', label: 'Incêndios ativos', symbol: 'flame.fill', glyph: 'Fog' },
  { metric: 'totalMan', label: 'Operacionais', symbol: 'person.2.fill', glyph: 'Op' },
  { metric: 'totalTerrain', label: 'Meios terrestres', symbol: 'truck.box.fill', glyph: 'Ter' },
  { metric: 'totalAerial', label: 'Meios aéreos', symbol: 'airplane', glyph: 'Aé' },
]

export default function SituacaoScreen() {
  const c = useTheme() as ThemeColors
  const insets = useSafeAreaInsets()

  const query = useQuery(situationReportsQueryOptions(14))
  const reports = query.data ?? []
  const latest = reports[0]
  const previous = reports[1]
  const earlier = reports.slice(1)

  return (
    <ScrollView
      style={{ backgroundColor: c.background }}
      contentContainerStyle={[
        styles.content,
        { paddingBottom: insets.bottom + Spacing.six },
      ]}
      contentInsetAdjustmentBehavior="automatic"
    >
      {query.isLoading && !latest ? (
        <View style={styles.state}>
          <ActivityIndicator color={c.textSecondary} />
        </View>
      ) : latest ? (
        <>
          <Hero report={latest} previous={previous} c={c} />
          <TopIncidents ids={latest.topIncidentIds} c={c} />
          {earlier.length > 0 && <EarlierReports reports={earlier} c={c} />}
        </>
      ) : query.isError ? (
        <View style={styles.state}>
          <Text style={[styles.stateText, { color: c.textSecondary }]}>
            Não foi possível carregar o ponto de situação.
          </Text>
          <Pressable onPress={() => query.refetch()} hitSlop={8}>
            <Text style={[styles.stateAction, { color: ACCENT }]}>
              Tentar novamente
            </Text>
          </Pressable>
        </View>
      ) : (
        <View style={styles.state}>
          <Text style={[styles.stateText, { color: c.textSecondary }]}>
            Ainda não há relatórios de situação.
          </Text>
        </View>
      )}
    </ScrollView>
  )
}

// ── Hero — the latest report ─────────────────────────────────────────────────

function Hero({
  report,
  previous,
  c,
}: {
  report: SituationReport
  previous: SituationReport | undefined
  c: ThemeColors
}) {
  const deltas = previous ? situationDeltas(report, previous) : []
  const deltaByMetric = new Map<SituationMetric, SituationDelta>()
  for (const d of deltas) deltaByMetric.set(d.metric, d)

  return (
    <View style={[styles.hero, { backgroundColor: c.backgroundElement }]}>
      <View style={styles.heroHead}>
        <Text style={[styles.heroTitle, { color: c.text }]}>
          Ponto de situação · {situationSlotLabel(report.slot)}
        </Text>
        <Text style={[styles.heroStamp, { color: c.textSecondary }]}>
          Atualizado às {formatClock(report.at)}
        </Text>
      </View>

      <View style={styles.tiles}>
        {STAT_TILES.map(({ metric, label, symbol, glyph }) => {
          const delta = deltaByMetric.get(metric)
          return (
            <StatTile
              key={metric}
              label={label}
              value={formatInteger(report[metric])}
              symbol={symbol}
              glyph={glyph}
              delta={
                delta
                  ? { text: formatSignedInteger(delta.diff), tone: delta.tone }
                  : undefined
              }
              c={c}
            />
          )
        })}
      </View>

      <Text style={[styles.body, { color: c.text }]}>
        {report.body.replace(/\r\n/g, '\n')}
      </Text>
    </View>
  )
}

// ── Top incidents ────────────────────────────────────────────────────────────

function TopIncidents({ ids, c }: { ids: string[]; c: ThemeColors }) {
  const router = useRouter()
  // Cross-reference the report's top-fire ids against the live recent feed so
  // each links to the map with a name and status. Ids windowed out of the feed
  // render nothing — exactly as web.
  const query = useQuery(recentIncidentsQueryOptions())
  const byId = new Map<string, IncidentListItem>()
  for (const inc of query.data ?? []) byId.set(inc.id, inc)

  const matched = ids
    .map((id) => byId.get(id))
    .filter((inc): inc is IncidentListItem => inc !== undefined)

  const openIncident = useCallback(
    (id: string) => {
      router.navigate({ pathname: '/', params: { incident: id } })
    },
    [router],
  )

  if (matched.length === 0) return null

  return (
    <View style={styles.section}>
      <Text style={[styles.sectionTitle, { color: c.text }]}>
        Maiores ocorrências
      </Text>
      <View style={styles.incidentList}>
        {matched.map((inc) => {
          const place =
            locationParts(inc.freguesia, inc.concelho, inc.district) ||
            inc.location
          return (
            <Pressable
              key={inc.id}
              onPress={() => openIncident(inc.id)}
              accessibilityRole="button"
              accessibilityLabel={incidentTitle(inc)}
              style={({ pressed }) => [
                styles.incidentRow,
                { backgroundColor: c.backgroundElement },
                pressed && { opacity: 0.6 },
              ]}
            >
              <View
                style={[
                  styles.dot,
                  { backgroundColor: statusColorForCode(inc.status.code) },
                ]}
              />
              <View style={styles.incidentBody}>
                <Text numberOfLines={1} style={[styles.incidentTitle, { color: c.text }]}>
                  {incidentTitle(inc)}
                </Text>
                {place.length > 0 && (
                  <Text
                    numberOfLines={1}
                    style={[styles.incidentPlace, { color: c.textSecondary }]}
                  >
                    {place}
                  </Text>
                )}
              </View>
              <Text style={[styles.incidentStatus, { color: c.textSecondary }]}>
                {inc.status.label}
              </Text>
            </Pressable>
          )
        })}
      </View>
    </View>
  )
}

// ── Earlier reports ──────────────────────────────────────────────────────────

function EarlierReports({
  reports,
  c,
}: {
  reports: SituationReport[]
  c: ThemeColors
}) {
  return (
    <View style={styles.section}>
      <Text style={[styles.sectionTitle, { color: c.text }]}>
        Relatórios anteriores
      </Text>
      <View style={styles.earlierList}>
        {reports.map((report) => (
          <EarlierReportRow key={report.id} report={report} c={c} />
        ))}
      </View>
    </View>
  )
}

function EarlierReportRow({
  report,
  c,
}: {
  report: SituationReport
  c: ThemeColors
}) {
  const [open, setOpen] = useState(false)
  return (
    <View style={[styles.earlier, { backgroundColor: c.backgroundElement }]}>
      <Pressable
        onPress={() => setOpen((v) => !v)}
        accessibilityRole="button"
        accessibilityState={{ expanded: open }}
        style={styles.earlierHead}
      >
        <View style={styles.earlierSummary}>
          <Text style={[styles.earlierTitle, { color: c.text }]}>
            {situationSlotLabel(report.slot)} · {formatDayMonth(report.at)}
          </Text>
          <View style={styles.earlierCounts}>
            <Text style={[styles.earlierCount, { color: c.textSecondary }]}>
              {formatInteger(report.activeFires)} fogos
            </Text>
            <Text style={[styles.earlierCount, { color: c.textSecondary }]}>
              {formatInteger(report.totalMan)} operacionais
            </Text>
            <Text style={[styles.earlierCount, { color: c.textSecondary }]}>
              {formatInteger(report.totalTerrain)} terrestres
            </Text>
            <Text style={[styles.earlierCount, { color: c.textSecondary }]}>
              {formatInteger(report.totalAerial)} aéreos
            </Text>
          </View>
        </View>
        <SymbolView
          name={open ? 'chevron.up' : 'chevron.down'}
          size={13}
          tintColor={c.textSecondary}
          fallback={
            <Text style={[styles.earlierChevron, { color: c.textSecondary }]}>
              {open ? '▲' : '▼'}
            </Text>
          }
        />
      </Pressable>
      {open && (
        <Text
          style={[
            styles.earlierBody,
            { color: c.text, borderTopColor: c.backgroundSelected },
          ]}
        >
          {report.body.replace(/\r\n/g, '\n')}
        </Text>
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
    gap: Spacing.four,
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
  hero: {
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.three,
  },
  heroHead: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    alignItems: 'baseline',
    justifyContent: 'space-between',
    gap: Spacing.two,
  },
  heroTitle: {
    fontSize: 15,
    fontWeight: '600',
  },
  heroStamp: {
    fontSize: 13,
    fontVariant: ['tabular-nums'],
  },
  tiles: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  body: {
    fontSize: 15,
    lineHeight: 22,
  },
  section: {
    gap: Spacing.three,
  },
  sectionTitle: {
    fontSize: 16,
    fontWeight: '600',
  },
  incidentList: {
    gap: Spacing.two,
  },
  incidentRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
  },
  dot: {
    width: 10,
    height: 10,
    borderRadius: 999,
  },
  incidentBody: {
    flex: 1,
    gap: 1,
  },
  incidentTitle: {
    fontSize: 15,
    fontWeight: '600',
  },
  incidentPlace: {
    fontSize: 13,
  },
  incidentStatus: {
    fontSize: 12,
  },
  earlierList: {
    gap: Spacing.two,
  },
  earlier: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.three,
  },
  earlierHead: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
  },
  earlierSummary: {
    flex: 1,
    gap: Spacing.one,
  },
  earlierTitle: {
    fontSize: 15,
    fontWeight: '500',
  },
  earlierCounts: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  earlierCount: {
    fontSize: 12,
    fontVariant: ['tabular-nums'],
  },
  earlierChevron: {
    fontSize: 11,
  },
  earlierBody: {
    marginTop: Spacing.three,
    paddingTop: Spacing.three,
    borderTopWidth: StyleSheet.hairlineWidth,
    fontSize: 15,
    lineHeight: 22,
  },
})
