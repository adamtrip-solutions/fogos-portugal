import { useCallback, useMemo, useRef, useState } from 'react'
import {
  ActivityIndicator,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'

import { FireMap } from '@/components/fire-map'
import { IncidentSheet, type IncidentSheetRef } from '@/components/incident-sheet'
import { Colors, Spacing } from '@/constants/theme'
import { useActiveIncidents } from '@/hooks/use-active-incidents'

export default function MapScreen() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const insets = useSafeAreaInsets()

  const { incidents, loading, error } = useActiveIncidents()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const sheetRef = useRef<IncidentSheetRef>(null)

  const selected = useMemo(
    () => incidents.find((i) => i.id === selectedId) ?? null,
    [incidents, selectedId],
  )

  const handleSelect = useCallback((id: string) => {
    setSelectedId(id)
    sheetRef.current?.present()
  }, [])

  const handleClose = useCallback(() => setSelectedId(null), [])

  return (
    <View style={styles.container}>
      <FireMap incidents={incidents} isDark={scheme === 'dark'} onSelect={handleSelect} />

      {(loading || error) && (
        <View
          style={[
            styles.statusPill,
            { top: insets.top + Spacing.two, backgroundColor: c.backgroundElement },
          ]}
        >
          {loading && <ActivityIndicator size="small" color={c.text} />}
          <Text style={[styles.statusText, { color: c.text }]}>
            {loading ? 'A carregar…' : 'Sem ligação à API'}
          </Text>
        </View>
      )}

      <IncidentSheet ref={sheetRef} incident={selected} onClose={handleClose} />
    </View>
  )
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
  },
  statusPill: {
    position: 'absolute',
    alignSelf: 'center',
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.two,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    borderRadius: 999,
  },
  statusText: {
    fontSize: 14,
    fontWeight: '600',
  },
})
