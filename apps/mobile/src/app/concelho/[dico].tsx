import { useCallback } from 'react'
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
import { Stack, useLocalSearchParams, useRouter } from 'expo-router'

import { yoyRatio } from '@fogos/api-client'
import type { ConcelhoProfile } from '@fogos/api-client'
import { formatHectares, formatInteger, formatSignedPercent } from '@fogos/ui-tokens'

import { IncidentCard } from '@/components/incident-card'
import { RiskStrip } from '@/components/risk-strip'
import { StatTile } from '@/components/stats/stat-tile'
import { WarningRow } from '@/components/warning-row'
import type { ThemeColors } from '@/components/incident/section'
import { Colors, Spacing } from '@/constants/theme'
import { useConcelhoProfile } from '@/hooks/use-concelho-profile'
import { currentSeasonYear } from '@/hooks/use-season-stats'

const ACCENT = '#FF6E02'

/** Coerce the `dico` route param (string | string[]) to a single value. */
function dicoParam(raw: string | string[] | undefined): string {
  return (Array.isArray(raw) ? raw[0] : raw) ?? ''
}

export default function ConcelhoScreen() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const insets = useSafeAreaInsets()
  const router = useRouter()

  const dico = dicoParam(useLocalSearchParams<{ dico: string }>().dico)
  const { data, isLoading, isError } = useConcelhoProfile(dico)

  const openIncident = useCallback(
    (id: string) => {
      router.navigate({ pathname: '/', params: { incident: id } })
    },
    [router],
  )

  return (
    <>
      {/* Header title follows the concelho name once it loads. */}
      <Stack.Screen options={{ title: data?.name ?? 'Concelho' }} />
      <ScrollView
        style={{ backgroundColor: c.background }}
        contentContainerStyle={[
          styles.content,
          { paddingBottom: insets.bottom + Spacing.six },
        ]}
        contentInsetAdjustmentBehavior="automatic"
        showsVerticalScrollIndicator={false}
      >
        {isLoading && !data ? (
          <View style={styles.state}>
            <ActivityIndicator color={c.textSecondary} />
          </View>
        ) : !data ? (
          <View style={styles.state}>
            <Text style={[styles.stateText, { color: c.textSecondary }]}>
              {isError
                ? 'Não foi possível carregar o concelho. A tentar novamente…'
                : 'Concelho desconhecido.'}
            </Text>
            <Pressable
              onPress={() => router.navigate('/')}
              accessibilityRole="button"
              hitSlop={8}
            >
              <Text style={[styles.stateAction, { color: ACCENT }]}>
                Voltar ao mapa
              </Text>
            </Pressable>
          </View>
        ) : (
          <Profile
            profile={data}
            scheme={scheme}
            c={c}
            onOpenIncident={openIncident}
          />
        )}
      </ScrollView>
    </>
  )
}

function Profile({
  profile,
  scheme,
  c,
  onOpenIncident,
}: {
  profile: ConcelhoProfile
  scheme: 'light' | 'dark'
  c: ThemeColors
  onOpenIncident: (id: string) => void
}) {
  const ratio = yoyRatio(profile.yearIgnitions, profile.previousYearIgnitions)
  const delta =
    ratio == null
      ? undefined
      : {
          text: formatSignedPercent(ratio),
          // More ignitions is worse (red); fewer is better (green).
          tone: (ratio > 0 ? 'bad' : ratio < 0 ? 'good' : 'neutral') as
            | 'good'
            | 'bad'
            | 'neutral',
        }
  // The backend counts ignitions on the Lisbon calendar year — use the shared
  // helper so the label matches (not the device-local year).
  const year = currentSeasonYear()

  return (
    <View style={styles.sections}>
      {/* Header */}
      <View style={styles.header}>
        <Text style={[styles.district, { color: c.textSecondary }]}>
          {profile.district}
        </Text>
        <Text style={[styles.name, { color: c.text }]}>{profile.name}</Text>
      </View>

      {/* 5-day risk strip */}
      {profile.risk.length > 0 && (
        <View style={styles.section}>
          <Text style={[styles.heading, { color: c.text }]}>Risco de incêndio</Text>
          <RiskStrip risk={profile.risk} c={c} />
        </View>
      )}

      {/* YoY tiles */}
      <View style={styles.tiles}>
        <StatTile
          label={`Ignições em ${year}`}
          value={formatInteger(profile.yearIgnitions)}
          hint={`${formatInteger(profile.previousYearIgnitions)} no ano anterior`}
          delta={delta}
          c={c}
        />
        <StatTile
          label="Ano anterior (mesmo período)"
          value={formatInteger(profile.previousYearIgnitions)}
          c={c}
        />
        <StatTile
          label="Área ardida no ano"
          value={formatHectares(profile.yearBurnAreaHa)}
          full
          c={c}
        />
      </View>

      {/* Active incidents */}
      <View style={styles.section}>
        <Text style={[styles.heading, { color: c.text }]}>Ocorrências ativas</Text>
        {profile.activeIncidents.length === 0 ? (
          <View style={[styles.empty, { backgroundColor: c.backgroundElement }]}>
            <Text style={[styles.emptyText, { color: c.textSecondary }]}>
              Sem ocorrências ativas neste concelho.
            </Text>
          </View>
        ) : (
          <View style={styles.incidents}>
            {profile.activeIncidents.map((inc) => (
              <IncidentCard
                key={inc.id}
                incident={inc}
                scheme={scheme}
                c={c}
                onPress={onOpenIncident}
              />
            ))}
          </View>
        )}
      </View>

      {/* IPMA warnings */}
      {profile.weatherWarnings.length > 0 && (
        <View style={styles.section}>
          <Text style={[styles.heading, { color: c.text }]}>
            Avisos meteorológicos (IPMA)
          </Text>
          <View style={[styles.card, { backgroundColor: c.backgroundElement }]}>
            {profile.weatherWarnings.map((w) => (
              <WarningRow key={w.id} warning={w} c={c} />
            ))}
          </View>
        </View>
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
  },
  sections: {
    gap: Spacing.four,
  },
  header: {
    gap: Spacing.half,
  },
  district: {
    fontSize: 14,
    fontWeight: '500',
  },
  name: {
    fontSize: 26,
    fontWeight: '700',
  },
  section: {
    gap: Spacing.three,
  },
  heading: {
    fontSize: 16,
    fontWeight: '600',
  },
  tiles: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.three,
  },
  incidents: {
    gap: Spacing.two,
  },
  empty: {
    borderRadius: Spacing.three,
    paddingVertical: Spacing.four,
    paddingHorizontal: Spacing.three,
    alignItems: 'center',
  },
  emptyText: {
    fontSize: 14,
    textAlign: 'center',
  },
  card: {
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.three,
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
})
