import { useCallback, useMemo, useRef, useState } from 'react'
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useRouter } from 'expo-router'
import { SymbolView } from 'expo-symbols'
import { useQuery } from '@tanstack/react-query'

import { concelhoByDico } from '@fogos/api-client'
import type { RiskDay, RiskFeature } from '@fogos/api-client'
import { RISK_LEVELS, RISK_STYLE, RISK_UNKNOWN, riskLabel, riskStyle } from '@fogos/ui-tokens'

import {
  ConcelhoSearchSheet,
  type ConcelhoSearchSheetRef,
} from '@/components/concelho-search-sheet'
import { RiskMap, type RiskMapRef } from '@/components/risk-map'
import { Colors, Spacing } from '@/constants/theme'
import { fireRiskQueryOptions } from '@/hooks/use-fire-risk'

const ACCENT = '#FF6E02'

// Day segmented control — labels verbatim from web's DAY_PILLS.
const DAY_PILLS: { label: string; value: RiskDay }[] = [
  { label: 'Hoje', value: 'TODAY' },
  { label: 'Amanhã', value: 'TOMORROW' },
  { label: 'Depois de amanhã', value: 'AFTER' },
]

// Legend rows — the five levels plus the "Sem dados" swatch (6 total).
const LEGEND_ROWS: { bg: string; label: string }[] = [
  ...RISK_LEVELS.map((l) => ({ bg: RISK_STYLE[l].bg, label: RISK_STYLE[l].label })),
  { bg: RISK_UNKNOWN.bg, label: RISK_UNKNOWN.label },
]

const forecastFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
})

/** "Previsão de 7 jul." for a stored YYYY-MM-DD forecast date. */
function formatForecast(date: string | null | undefined): string | null {
  if (!date) return null
  return `Previsão de ${forecastFmt.format(new Date(`${date}T00:00:00`))}`
}

/** Bounding-box centre of a concelho polygon — the camera target for search. */
function featureCentroid(feature: RiskFeature): [number, number] {
  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  const rings =
    feature.geometry.type === 'Polygon'
      ? feature.geometry.coordinates
      : feature.geometry.coordinates.flat()
  for (const ring of rings) {
    for (const [x, y] of ring) {
      if (x < minX) minX = x
      if (x > maxX) maxX = x
      if (y < minY) minY = y
      if (y > maxY) maxY = y
    }
  }
  return [(minX + maxX) / 2, (minY + maxY) / 2]
}

export default function RiscoScreen() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const insets = useSafeAreaInsets()
  const router = useRouter()

  const [day, setDay] = useState<RiskDay>('TODAY')
  const [selectedDico, setSelectedDico] = useState<string | null>(null)

  const mapRef = useRef<RiskMapRef>(null)
  const searchRef = useRef<ConcelhoSearchSheetRef>(null)

  const risk = useQuery(fireRiskQueryOptions(day))
  const geoJson = risk.data?.geoJson ?? null
  const forecast = formatForecast(risk.data?.forecastDate)

  const selectedFeature = useMemo(
    () =>
      selectedDico && geoJson
        ? geoJson.features.find((f) => f.properties.dico === selectedDico) ?? null
        : null,
    [selectedDico, geoJson],
  )

  // Fly the camera to a concelho picked from search (a tapped concelho is already
  // framed). Falls back to just selecting when the horizon has no polygon for it.
  const flyToDico = useCallback(
    (dico: string) => {
      const feature = geoJson?.features.find((f) => f.properties.dico === dico)
      if (feature) mapRef.current?.focus(featureCentroid(feature))
    },
    [geoJson],
  )

  const handleSearchSelect = useCallback(
    (dico: string) => {
      setSelectedDico(dico)
      flyToDico(dico)
    },
    [flyToDico],
  )

  const openProfile = useCallback(
    (dico: string) => {
      router.push({ pathname: '/concelho/[dico]', params: { dico } })
    },
    [router],
  )

  const fallback = selectedDico ? concelhoByDico(selectedDico) : null
  const cardName = selectedFeature?.properties.name ?? fallback?.name ?? 'Concelho'
  const cardDistrict = fallback?.district ?? ''
  const cardLevel = selectedFeature?.properties.level ?? 0

  return (
    <View style={[styles.container, { backgroundColor: c.background }]}>
      {/* Controls */}
      <View style={styles.controls}>
        <Text style={[styles.forecast, { color: c.textSecondary }]}>
          {forecast ?? 'Índice de risco de incêndio rural (IPMA)'}
        </Text>

        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          contentContainerStyle={styles.pills}
        >
          {DAY_PILLS.map((pill) => {
            const active = day === pill.value
            return (
              <Pressable
                key={pill.value}
                onPress={() => setDay(pill.value)}
                accessibilityRole="button"
                accessibilityState={{ selected: active }}
                style={[
                  styles.pill,
                  { backgroundColor: active ? ACCENT : c.backgroundElement },
                ]}
              >
                <Text
                  style={[
                    styles.pillLabel,
                    { color: active ? '#ffffff' : c.textSecondary },
                  ]}
                >
                  {pill.label}
                </Text>
              </Pressable>
            )
          })}
        </ScrollView>

        <Pressable
          onPress={() => searchRef.current?.present()}
          accessibilityRole="button"
          accessibilityLabel="Procurar concelho"
          style={[styles.searchPill, { backgroundColor: c.backgroundElement }]}
        >
          <SymbolView
            name="magnifyingglass"
            size={15}
            tintColor={c.textSecondary}
            fallback={<Text style={{ color: c.textSecondary }}>⌕</Text>}
          />
          <Text style={[styles.searchText, { color: c.textSecondary }]}>
            Procurar concelho
          </Text>
        </Pressable>
      </View>

      {/* Map */}
      <View style={styles.mapWrap}>
        {geoJson ? (
          <RiskMap
            ref={mapRef}
            data={geoJson}
            isDark={scheme === 'dark'}
            selectedDico={selectedDico}
            onSelect={setSelectedDico}
          />
        ) : (
          <View style={[styles.mapState, { backgroundColor: c.backgroundElement }]}>
            {risk.isLoading ? (
              <ActivityIndicator color={c.textSecondary} />
            ) : risk.isError ? (
              <>
                <Text style={[styles.mapStateText, { color: c.textSecondary }]}>
                  Não foi possível carregar o risco de incêndio.
                </Text>
                <Pressable onPress={() => risk.refetch()} hitSlop={8}>
                  <Text style={[styles.mapStateAction, { color: ACCENT }]}>
                    Tentar novamente
                  </Text>
                </Pressable>
              </>
            ) : (
              <Text style={[styles.mapStateText, { color: c.textSecondary }]}>
                Sem previsão disponível de momento.
              </Text>
            )}
          </View>
        )}

        {/* Selected-concelho card */}
        {selectedDico && (
          <View style={[styles.card, { backgroundColor: c.background }]}>
            <View style={styles.cardHead}>
              <View style={styles.cardTitleWrap}>
                {cardDistrict.length > 0 && (
                  <Text style={[styles.cardDistrict, { color: c.textSecondary }]}>
                    {cardDistrict}
                  </Text>
                )}
                <Text style={[styles.cardName, { color: c.text }]} numberOfLines={1}>
                  {cardName}
                </Text>
              </View>
              <Pressable
                onPress={() => setSelectedDico(null)}
                accessibilityRole="button"
                accessibilityLabel="Fechar"
                hitSlop={8}
                style={[styles.close, { backgroundColor: c.backgroundElement }]}
              >
                <SymbolView
                  name="xmark"
                  size={13}
                  tintColor={c.textSecondary}
                  fallback={<Text style={{ color: c.textSecondary }}>✕</Text>}
                />
              </Pressable>
            </View>

            <View style={styles.cardRow}>
              <View style={[styles.levelChip, { backgroundColor: `${riskStyle(cardLevel).bg}22` }]}>
                <View style={[styles.levelDot, { backgroundColor: riskStyle(cardLevel).bg }]} />
                <Text style={[styles.levelLabel, { color: c.text }]}>
                  {riskLabel(cardLevel)}
                </Text>
              </View>
              <Pressable
                onPress={() => openProfile(selectedDico)}
                accessibilityRole="button"
                style={styles.verButton}
              >
                <Text style={[styles.verLabel, { color: ACCENT }]}>Ver concelho</Text>
                <SymbolView
                  name="arrow.right"
                  size={13}
                  tintColor={ACCENT}
                  fallback={<Text style={{ color: ACCENT }}>→</Text>}
                />
              </Pressable>
            </View>
          </View>
        )}
      </View>

      {/* Legend */}
      <View
        style={[
          styles.legend,
          { paddingBottom: insets.bottom + Spacing.two },
        ]}
      >
        {LEGEND_ROWS.map((row) => (
          <View key={row.label} style={styles.legendItem}>
            <View style={[styles.legendDot, { backgroundColor: row.bg }]} />
            <Text style={[styles.legendLabel, { color: c.textSecondary }]}>
              {row.label}
            </Text>
          </View>
        ))}
      </View>

      <ConcelhoSearchSheet ref={searchRef} onSelect={handleSearchSelect} />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  controls: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
    paddingBottom: Spacing.three,
    gap: Spacing.three,
  },
  forecast: {
    fontSize: 14,
  },
  pills: {
    gap: Spacing.two,
  },
  pill: {
    borderRadius: 999,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
  },
  pillLabel: {
    fontSize: 14,
    fontWeight: '600',
  },
  searchPill: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
  },
  searchText: {
    fontSize: 15,
  },
  mapWrap: {
    flex: 1,
    overflow: 'hidden',
  },
  mapState: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    alignItems: 'center',
    justifyContent: 'center',
    gap: Spacing.three,
    paddingHorizontal: Spacing.four,
  },
  mapStateText: {
    fontSize: 14,
    textAlign: 'center',
  },
  mapStateAction: {
    fontSize: 14,
    fontWeight: '600',
  },
  card: {
    position: 'absolute',
    left: Spacing.three,
    right: Spacing.three,
    bottom: Spacing.three,
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.three,
    shadowColor: '#000',
    shadowOpacity: 0.18,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 4 },
    elevation: 6,
  },
  cardHead: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: Spacing.two,
  },
  cardTitleWrap: {
    flex: 1,
    gap: Spacing.half,
  },
  cardDistrict: {
    fontSize: 12,
    fontWeight: '500',
  },
  cardName: {
    fontSize: 18,
    fontWeight: '700',
  },
  close: {
    width: 28,
    height: 28,
    borderRadius: 999,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cardRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.two,
  },
  levelChip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    borderRadius: 999,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.one + 2,
  },
  levelDot: {
    width: 10,
    height: 10,
    borderRadius: 999,
  },
  levelLabel: {
    fontSize: 13,
    fontWeight: '600',
  },
  verButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.one,
    paddingHorizontal: Spacing.two,
    paddingVertical: Spacing.one,
  },
  verLabel: {
    fontSize: 14,
    fontWeight: '700',
  },
  legend: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.two,
  },
  legendItem: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.one + 2,
  },
  legendDot: {
    width: 11,
    height: 11,
    borderRadius: 999,
  },
  legendLabel: {
    fontSize: 12,
  },
})
