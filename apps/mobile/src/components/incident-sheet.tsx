import { forwardRef, useCallback, type ComponentRef } from 'react'
import {
  ActivityIndicator,
  Platform,
  Pressable,
  Share,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useRouter } from 'expo-router'
import { SymbolView } from 'expo-symbols'
import { BottomSheetModal, BottomSheetScrollView } from '@gorhom/bottom-sheet'

import { Colors, Spacing } from '@/constants/theme'
import type { ThemeColors } from '@/components/incident/section'
import { incidentWebUrl } from '@/lib/fogos/config'
import {
  badgeNeedsDarkText,
  formatRelative,
  incidentTitle,
  statusColorForCode,
} from '@fogos/ui-tokens'
import { concelhoByName } from '@fogos/api-client'
import type { IncidentDetail, IncidentListItem } from '@fogos/api-client'

import { AircraftSection } from './incident/aircraft-section'
import { EvolutionSection } from './incident/evolution-section'
import { IcnfSection } from './incident/icnf-section'
import { PhotosSection } from './incident/photos-section'
import { ResourceChart } from './incident/resource-chart'
import { ResourcesSection } from './incident/resources-section'
import { SignalBadges } from './incident/signal-badges'
import { WeatherSection } from './incident/weather-section'

const ACCENT = '#FF6E02'

/** Imperative handle of the modal (present/dismiss/snapToIndex/…). */
export type IncidentSheetRef = ComponentRef<typeof BottomSheetModal>

interface IncidentSheetProps {
  /**
   * The tapped list item — the sheet renders instantly from this when the fire
   * is in the loaded feed. Null for a deep-linked fire outside the window, in
   * which case the sheet renders purely from `detail`.
   */
  incident: IncidentListItem | null
  /** Full detail for the selection; fills in progressively once it resolves. */
  detail: IncidentDetail | null
  /** True while the detail query is loading with nothing yet to show. */
  detailLoading: boolean
  /** True when the detail query errored (list-item header still shows). */
  detailError: boolean
  /**
   * True when the detail query SUCCEEDED but `incident(id)` came back null — a
   * bogus/purged deep-link id with no list item. Distinct from `detailError`
   * (a genuine fetch failure); drives the quiet "not found" line.
   */
  detailNotFound: boolean
  onClose: () => void
}

const SNAP_POINTS = ['45%', '92%']

/**
 * Full incident detail bottom sheet — the mobile port of web's `IncidentPanel`.
 * Renders immediately from the in-memory list item (header + resource tiles) and
 * fills detail sections (signals, resource-history chart, weather, ICNF, status
 * timeline, response times, aircraft, photos) as the `incident(id)` detail query
 * resolves. pt-PT copy throughout, mirroring the web panel.
 */
export const IncidentSheet = forwardRef<IncidentSheetRef, IncidentSheetProps>(
  function IncidentSheet(
    { incident, detail, detailLoading, detailError, detailNotFound, onClose },
    ref,
  ) {
    const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
    const c = Colors[scheme]
    const router = useRouter()

    // Render from the detail when present, else the list item (web's `base`).
    const base = detail ?? incident

    const badgeColor = base ? statusColorForCode(base.status.code) : '#000'
    const badgeText = badgeNeedsDarkText(badgeColor) ? '#18181b' : '#ffffff'

    // Resolve the incident's concelho name to a DICO so its segment of the
    // location line can link to the concelho profile (mirrors web's IncidentPanel;
    // null for islands / spelling drift → the segment stays plain text).
    const concelhoEntry = base?.concelho ? concelhoByName(base.concelho) : null

    // Share the canonical web URL — reuses the incident title helper (pt-PT).
    // RN Share platform semantics: iOS takes `url` separately + a short message;
    // Android has no `url` field, so fold the link into the message there.
    const handleShare = useCallback(() => {
      if (!base) return
      const url = incidentWebUrl(base.id)
      const title = incidentTitle(base)
      const message = `${title} — Fogos Portugal`
      void Share.share(
        Platform.OS === 'ios'
          ? { message, url }
          : { message: `${message}\n${url}` },
      )
    }, [base])

    return (
      <BottomSheetModal
        ref={ref}
        snapPoints={SNAP_POINTS}
        enableDynamicSizing={false}
        onDismiss={onClose}
        backgroundStyle={{ backgroundColor: c.background }}
        handleIndicatorStyle={{ backgroundColor: c.textSecondary }}
      >
        <BottomSheetScrollView
          contentContainerStyle={styles.content}
          showsVerticalScrollIndicator={false}
        >
          {!base && detailLoading && (
            <View style={styles.pending}>
              <ActivityIndicator color={c.textSecondary} />
            </View>
          )}

          {!base && !detailLoading && detailError && (
            <Text style={[styles.errorLine, { color: c.textSecondary }]}>
              Não foi possível carregar esta ocorrência
            </Text>
          )}

          {/* Deep link to a fire that no longer exists (query succeeded, null). */}
          {!base && !detailLoading && !detailError && detailNotFound && (
            <Text style={[styles.errorLine, { color: c.textSecondary }]}>
              Não foi possível carregar esta ocorrência.
            </Text>
          )}

          {base && (
            <>
              {/* 1. Header */}
              <View style={styles.header}>
                <View style={styles.badgeRow}>
                  <View style={[styles.badge, { backgroundColor: badgeColor }]}>
                    <Text style={[styles.badgeLabel, { color: badgeText }]}>
                      {base.status.label}
                    </Text>
                  </View>
                  <Pressable
                    accessibilityRole="button"
                    accessibilityLabel="Partilhar ocorrência"
                    hitSlop={8}
                    onPress={handleShare}
                    style={[
                      styles.shareButton,
                      { backgroundColor: c.backgroundElement },
                    ]}
                  >
                    <SymbolView
                      name="square.and.arrow.up"
                      size={18}
                      tintColor={c.text}
                      fallback={
                        <Text style={[styles.shareGlyph, { color: c.text }]}>
                          ↑
                        </Text>
                      }
                    />
                  </Pressable>
                </View>
                <Text style={[styles.title, { color: c.text }]}>
                  {incidentTitle(base)}
                </Text>
                <LocationLine
                  freguesia={base.freguesia}
                  concelho={base.concelho}
                  district={base.district}
                  dico={concelhoEntry?.dico ?? null}
                  c={c}
                  onPressConcelho={() => {
                    if (concelhoEntry) {
                      router.push({
                        pathname: '/concelho/[dico]',
                        params: { dico: concelhoEntry.dico },
                      })
                    }
                  }}
                />
                <Text style={[styles.time, { color: c.textSecondary }]}>
                  Iniciado {formatRelative(base.occurredAt)}
                </Text>
                <Text style={[styles.time, { color: c.textSecondary }]}>
                  Atualizado {formatRelative(base.updatedAt)}
                </Text>

                {/* 2. Signal badges */}
                <SignalBadges
                  escalating={base.signals.escalating}
                  rekindle={base.signals.rekindle}
                  criticalConditions={base.signals.criticalConditions}
                  reasons={detail?.signals.criticalReasons ?? []}
                  scheme={scheme}
                  c={c}
                />
              </View>

              {base.important && (
                <View style={styles.important}>
                  <Text
                    style={[
                      styles.importantText,
                      { color: scheme === 'dark' ? '#fcd34d' : '#b45309' },
                    ]}
                  >
                    Ocorrência importante
                  </Text>
                </View>
              )}

              {/* 3. Resources */}
              <ResourcesSection resources={base.resources} c={c} />

              {/* 4. Resource-history chart (detail only) */}
              {detail && (
                <ResourceChart
                  history={detail.history}
                  startedAt={detail.occurredAt}
                  current={{
                    at: detail.updatedAt,
                    man: detail.resources.man,
                    terrain: detail.resources.terrain,
                    aerial: detail.resources.aerial,
                  }}
                  c={c}
                />
              )}

              {/* 5. Weather */}
              {detail?.weather && <WeatherSection weather={detail.weather} c={c} />}

              {/* 6. ICNF */}
              {detail?.icnf && <IcnfSection icnf={detail.icnf} c={c} />}

              {/* 7 + 8. Status timeline + response times */}
              {detail && (
                <EvolutionSection
                  history={detail.statusHistory}
                  responseTimes={detail.responseTimes}
                  occurredAt={detail.occurredAt}
                  c={c}
                />
              )}

              {/* 9. Aircraft */}
              {detail && <AircraftSection aircraft={detail.aircraft} c={c} />}

              {/* 10. Photos */}
              {detail && detail.photos.length > 0 && (
                <PhotosSection photos={detail.photos} c={c} />
              )}

              {detailError && (
                <Text style={[styles.errorLine, { color: c.textSecondary }]}>
                  Não foi possível carregar todos os detalhes
                </Text>
              )}
            </>
          )}
        </BottomSheetScrollView>
      </BottomSheetModal>
    )
  },
)

/**
 * The freguesia · concelho · district line, with the concelho segment rendered as
 * a tappable link when its DICO resolves (native mirror of web's IncidentPanel
 * location line). Empties/repeats are dropped, mirroring `locationParts`.
 */
function LocationLine({
  freguesia,
  concelho,
  district,
  dico,
  c,
  onPressConcelho,
}: {
  freguesia: string | null
  concelho: string | null
  district: string | null
  dico: string | null
  c: ThemeColors
  onPressConcelho: () => void
}) {
  const parts: { value: string; isConcelho: boolean }[] = []
  for (const [value, isConcelho] of [
    [freguesia, false],
    [concelho, true],
    [district, false],
  ] as const) {
    const v = value?.trim()
    if (!v) continue
    if (parts.some((p) => p.value.toLowerCase() === v.toLowerCase())) continue
    parts.push({ value: v, isConcelho })
  }
  if (parts.length === 0) return null

  return (
    <Text style={[styles.place, { color: c.textSecondary }]}>
      {parts.map((part, i) => (
        <Text key={part.value}>
          {i > 0 ? ' · ' : ''}
          {part.isConcelho && dico ? (
            <Text style={styles.placeLink} onPress={onPressConcelho}>
              {part.value}
            </Text>
          ) : (
            part.value
          )}
        </Text>
      ))}
    </Text>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingBottom: Spacing.six,
    gap: Spacing.four,
  },
  placeLink: {
    color: ACCENT,
    fontWeight: '600',
  },
  header: {
    gap: Spacing.one,
  },
  pending: {
    paddingVertical: Spacing.six,
    alignItems: 'center',
  },
  badgeRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.two,
  },
  badge: {
    alignSelf: 'flex-start',
    borderRadius: 999,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.one,
  },
  shareButton: {
    width: 34,
    height: 34,
    borderRadius: 999,
    alignItems: 'center',
    justifyContent: 'center',
  },
  shareGlyph: {
    fontSize: 17,
    fontWeight: '700',
  },
  badgeLabel: {
    fontSize: 13,
    fontWeight: '700',
  },
  title: {
    fontSize: 22,
    fontWeight: '700',
    marginTop: Spacing.one,
  },
  place: {
    fontSize: 15,
  },
  time: {
    fontSize: 13,
  },
  important: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    backgroundColor: 'rgba(245,158,11,0.15)',
  },
  importantText: {
    fontSize: 14,
    fontWeight: '600',
  },
  errorLine: {
    fontSize: 13,
    textAlign: 'center',
  },
})
