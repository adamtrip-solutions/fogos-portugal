import { forwardRef, useMemo, type ComponentRef } from 'react'
import { StyleSheet, Text, View, useColorScheme } from 'react-native'
import { BottomSheetModal, BottomSheetView } from '@gorhom/bottom-sheet'

/** Imperative handle of the modal (present/dismiss/snapToIndex/…). */
export type IncidentSheetRef = ComponentRef<typeof BottomSheetModal>

import { Colors, Spacing } from '@/constants/theme'
import {
  badgeNeedsDarkText,
  formatRelative,
  hasResource,
  incidentTitle,
  locationParts,
  statusColorForCode,
} from '@/lib/fogos/format'
import type { IncidentListItem } from '@/lib/fogos/types'

// pt-PT resource-tile copy — mirrors the web incident panel's RESOURCE_TILES.
const RESOURCE_TILES = [
  { key: 'man', label: 'Operacionais' },
  { key: 'terrain', label: 'Meios terrestres' },
  { key: 'aerial', label: 'Meios aéreos' },
] as const

interface IncidentSheetProps {
  incident: IncidentListItem | null
  onClose: () => void
}

/**
 * Native bottom sheet with the incident detail subset the map needs: status
 * badge (bucket color), title, concelho/district line, "iniciado há X", and the
 * operacionais/terrestres/aéreos resource tiles. pt-PT throughout, mirroring the
 * web incident panel's wording.
 */
export const IncidentSheet = forwardRef<IncidentSheetRef, IncidentSheetProps>(
  function IncidentSheet({ incident, onClose }, ref) {
    const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
    const c = Colors[scheme]

    const badgeColor = incident ? statusColorForCode(incident.status.code) : '#000'
    const badgeText = badgeNeedsDarkText(badgeColor) ? '#18181b' : '#ffffff'

    const location = useMemo(
      () =>
        incident
          ? locationParts(
              incident.freguesia,
              incident.concelho,
              incident.district,
            )
          : '',
      [incident],
    )

    return (
      <BottomSheetModal
        ref={ref}
        snapPoints={['45%']}
        enableDynamicSizing={false}
        onDismiss={onClose}
        backgroundStyle={{ backgroundColor: c.background }}
        handleIndicatorStyle={{ backgroundColor: c.textSecondary }}
      >
        <BottomSheetView style={styles.content}>
          {incident && (
            <>
              <View style={[styles.badge, { backgroundColor: badgeColor }]}>
                <Text style={[styles.badgeLabel, { color: badgeText }]}>
                  {incident.status.label}
                </Text>
              </View>

              <Text style={[styles.title, { color: c.text }]}>
                {incidentTitle(incident)}
              </Text>
              {location.length > 0 && (
                <Text style={[styles.location, { color: c.textSecondary }]}>
                  {location}
                </Text>
              )}
              <Text style={[styles.time, { color: c.textSecondary }]}>
                Iniciado {formatRelative(incident.occurredAt)}
              </Text>

              <View style={styles.tiles}>
                {RESOURCE_TILES.map(({ key, label }) => {
                  const value = incident.resources[key]
                  return (
                    <View
                      key={key}
                      style={[styles.tile, { backgroundColor: c.backgroundElement }]}
                    >
                      <Text style={[styles.tileValue, { color: c.text }]}>
                        {value < 0 ? '—' : value}
                      </Text>
                      <Text style={[styles.tileLabel, { color: c.textSecondary }]}>
                        {label}
                      </Text>
                    </View>
                  )
                })}
              </View>
              {hasResource(incident.resources.aquatic) && (
                <Text style={[styles.aquatic, { color: c.textSecondary }]}>
                  Meios aquáticos: {incident.resources.aquatic}
                </Text>
              )}
            </>
          )}
        </BottomSheetView>
      </BottomSheetModal>
    )
  },
)

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingBottom: Spacing.five,
    gap: Spacing.two,
  },
  badge: {
    alignSelf: 'flex-start',
    borderRadius: 999,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.one,
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
  location: {
    fontSize: 15,
  },
  time: {
    fontSize: 14,
    marginBottom: Spacing.two,
  },
  tiles: {
    flexDirection: 'row',
    gap: Spacing.two,
    marginTop: Spacing.one,
  },
  tile: {
    flex: 1,
    borderRadius: Spacing.three,
    padding: Spacing.three,
    gap: Spacing.one,
  },
  tileValue: {
    fontSize: 26,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
  tileLabel: {
    fontSize: 12,
  },
  aquatic: {
    fontSize: 13,
    marginTop: Spacing.two,
  },
})
