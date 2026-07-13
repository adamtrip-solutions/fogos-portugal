import {
  forwardRef,
  useCallback,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react'
import { Pressable, StyleSheet, Text, View, useColorScheme } from 'react-native'
import { SymbolView } from 'expo-symbols'
import {
  BottomSheetFlatList,
  BottomSheetModal,
  BottomSheetTextInput,
} from '@gorhom/bottom-sheet'
import type { ComponentRef } from 'react'

import { INCIDENT_DISTRICTS, foldText } from '@fogos/api-client'

import { Colors, Spacing } from '@/constants/theme'

const ACCENT = '#FF6E02'

/** Imperative handle: the screen calls `present()` from the district trigger row. */
export interface DistrictPickerSheetRef {
  present: () => void
}

interface Row {
  /** District value, or null for the "Todos os distritos" option. */
  value: string | null
  label: string
}

const ALL_ROW: Row = { value: null, label: 'Todos os distritos' }

/**
 * Bottom-sheet district picker for the Ocorrências filter. A searchable list of
 * the 18 mainland + island districts (verbatim from @fogos/api-client), with
 * "Todos os distritos" pinned first. Selecting a row applies it and dismisses.
 */
export const DistrictPickerSheet = forwardRef<
  DistrictPickerSheetRef,
  { selected: string | null; onSelect: (value: string | null) => void }
>(function DistrictPickerSheet({ selected, onSelect }, ref) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const innerRef = useRef<ComponentRef<typeof BottomSheetModal>>(null)
  const [query, setQuery] = useState('')

  useImperativeHandle(
    ref,
    () => ({ present: () => innerRef.current?.present() }),
    [],
  )

  const rows = useMemo<Row[]>(() => {
    // Accent-insensitive fold (same helper as searchConcelhos) so an unaccented
    // query still finds Évora / Bragança / Santarém / Setúbal.
    const q = foldText(query)
    const matches = q
      ? INCIDENT_DISTRICTS.filter((d) => foldText(d).includes(q))
      : INCIDENT_DISTRICTS
    const items: Row[] = matches.map((d) => ({ value: d, label: d }))
    // Keep "Todos os distritos" first, and only while it matches the search.
    return q && !foldText(ALL_ROW.label).includes(q)
      ? items
      : [ALL_ROW, ...items]
  }, [query])

  const choose = useCallback(
    (value: string | null) => {
      onSelect(value)
      setQuery('')
      innerRef.current?.dismiss()
    },
    [onSelect],
  )

  return (
    <BottomSheetModal
      ref={innerRef}
      snapPoints={['70%']}
      enableDynamicSizing={false}
      backgroundStyle={{ backgroundColor: c.background }}
      handleIndicatorStyle={{ backgroundColor: c.textSecondary }}
    >
      <View style={styles.header}>
        <Text style={[styles.title, { color: c.text }]}>Distrito</Text>
        <BottomSheetTextInput
          value={query}
          onChangeText={setQuery}
          placeholder="Pesquisar distrito"
          placeholderTextColor={c.textSecondary}
          autoCorrect={false}
          style={[
            styles.search,
            { backgroundColor: c.backgroundElement, color: c.text },
          ]}
        />
      </View>
      <BottomSheetFlatList
        data={rows}
        keyExtractor={(item) => item.value ?? '__all__'}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.list}
        renderItem={({ item }) => {
          const active = item.value === selected
          return (
            <Pressable
              onPress={() => choose(item.value)}
              accessibilityRole="button"
              accessibilityState={{ selected: active }}
              style={({ pressed }) => [
                styles.row,
                pressed && { backgroundColor: c.backgroundElement },
              ]}
            >
              <Text style={[styles.rowLabel, { color: c.text }]}>{item.label}</Text>
              {active && (
                <SymbolView
                  name="checkmark"
                  size={15}
                  tintColor={ACCENT}
                  fallback={<Text style={styles.check}>✓</Text>}
                />
              )}
            </Pressable>
          )
        }}
      />
    </BottomSheetModal>
  )
})

const styles = StyleSheet.create({
  header: {
    paddingHorizontal: Spacing.four,
    paddingBottom: Spacing.two,
    gap: Spacing.two,
  },
  title: {
    fontSize: 20,
    fontWeight: '700',
  },
  search: {
    borderRadius: Spacing.three,
    paddingHorizontal: Spacing.three,
    paddingVertical: Spacing.two,
    fontSize: 16,
  },
  list: {
    paddingHorizontal: Spacing.four,
    paddingBottom: Spacing.six,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: Spacing.three,
  },
  rowLabel: {
    fontSize: 16,
    fontWeight: '500',
  },
  check: {
    fontSize: 14,
    fontWeight: '700',
    color: ACCENT,
  },
})
