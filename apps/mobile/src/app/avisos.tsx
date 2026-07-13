import { useCallback } from 'react'
import {
  ActivityIndicator,
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { SymbolView } from 'expo-symbols'
import { openBrowserAsync } from 'expo-web-browser'
import { useQuery } from '@tanstack/react-query'

import { groupWarningsByDistrict } from '@fogos/api-client'
import type { WarningDistrictGroup } from '@fogos/api-client'

import type { ThemeColors } from '@/components/incident/section'
import { WarningRow } from '@/components/warning-row'
import { Spacing } from '@/constants/theme'
import { useTheme } from '@/hooks/use-theme'
import { weatherWarningsQueryOptions } from '@/hooks/use-weather-warnings'

const ACCENT = '#FF6E02'
const IPMA_URL = 'https://www.ipma.pt'

export default function AvisosScreen() {
  const c = useTheme() as ThemeColors
  const insets = useSafeAreaInsets()

  const query = useQuery(weatherWarningsQueryOptions())
  const groups = groupWarningsByDistrict(query.data ?? [])

  const openIpma = useCallback(() => void openBrowserAsync(IPMA_URL), [])

  return (
    <ScrollView
      style={{ backgroundColor: c.background }}
      contentContainerStyle={[
        styles.content,
        { paddingBottom: insets.bottom + Spacing.six },
      ]}
      contentInsetAdjustmentBehavior="automatic"
    >
      <Text style={[styles.lead, { color: c.textSecondary }]}>
        Avisos oficiais do IPMA em vigor para Portugal continental.
      </Text>

      {query.isLoading && groups.length === 0 ? (
        <View style={styles.state}>
          <ActivityIndicator color={c.textSecondary} />
        </View>
      ) : query.isError && groups.length === 0 ? (
        <View style={styles.state}>
          <Text style={[styles.stateText, { color: c.textSecondary }]}>
            Não foi possível carregar os avisos.
          </Text>
          <Pressable onPress={() => query.refetch()} hitSlop={8}>
            <Text style={[styles.stateAction, { color: ACCENT }]}>
              Tentar novamente
            </Text>
          </Pressable>
        </View>
      ) : groups.length === 0 ? (
        <View style={styles.state}>
          <Text style={[styles.stateText, { color: c.textSecondary }]}>
            Sem avisos meteorológicos em vigor.
          </Text>
        </View>
      ) : (
        <View style={styles.groups}>
          {groups.map((group) => (
            <DistrictCard key={group.district} group={group} c={c} />
          ))}
        </View>
      )}

      <Pressable
        onPress={openIpma}
        accessibilityRole="link"
        accessibilityLabel="Fonte: IPMA"
        style={({ pressed }) => [styles.source, pressed && { opacity: 0.6 }]}
      >
        <Text style={[styles.sourceText, { color: c.textSecondary }]}>Fonte: IPMA</Text>
        <SymbolView
          name="arrow.up.right"
          size={12}
          tintColor={c.textSecondary}
          fallback={<Text style={[styles.sourceGlyph, { color: c.textSecondary }]}>↗</Text>}
        />
      </Pressable>
    </ScrollView>
  )
}

function DistrictCard({
  group,
  c,
}: {
  group: WarningDistrictGroup
  c: ThemeColors
}) {
  return (
    <View style={[styles.card, { backgroundColor: c.backgroundElement }]}>
      <Text style={[styles.district, { color: c.text }]}>{group.district}</Text>
      <View style={styles.rows}>
        {group.warnings.map((w) => (
          <WarningRow key={w.id} warning={w} c={c} />
        ))}
      </View>
    </View>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
    gap: Spacing.four,
  },
  lead: {
    fontSize: 15,
    lineHeight: 22,
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
  groups: {
    gap: Spacing.three,
  },
  card: {
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.three,
  },
  district: {
    fontSize: 16,
    fontWeight: '600',
  },
  rows: {
    gap: Spacing.three,
  },
  source: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'center',
    gap: Spacing.one,
    paddingVertical: Spacing.two,
  },
  sourceText: {
    fontSize: 13,
    fontWeight: '600',
  },
  sourceGlyph: {
    fontSize: 12,
  },
})
