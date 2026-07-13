import {
  forwardRef,
  useCallback,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react'
import { Pressable, StyleSheet, Text, View, useColorScheme } from 'react-native'
import {
  BottomSheetFlatList,
  BottomSheetModal,
  BottomSheetTextInput,
} from '@gorhom/bottom-sheet'
import type { ComponentRef } from 'react'

import { searchConcelhos } from '@fogos/api-client'
import type { ConcelhoEntry } from '@fogos/api-client'

import { Colors, Spacing } from '@/constants/theme'

/** Imperative handle: the screen calls `present()` from the search pill. */
export interface ConcelhoSearchSheetRef {
  present: () => void
}

/**
 * Bottom-sheet concelho search for the Risco screen — a diacritic-insensitive
 * search over the 278 mainland concelhos (via `searchConcelhos`), showing name +
 * district. Selecting a row applies its DICO and dismisses. Mirrors the
 * district-picker-sheet patterns.
 */
export const ConcelhoSearchSheet = forwardRef<
  ConcelhoSearchSheetRef,
  { onSelect: (dico: string) => void }
>(function ConcelhoSearchSheet({ onSelect }, ref) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const innerRef = useRef<ComponentRef<typeof BottomSheetModal>>(null)
  const [query, setQuery] = useState('')

  useImperativeHandle(
    ref,
    () => ({ present: () => innerRef.current?.present() }),
    [],
  )

  // Empty query surfaces the first slice; a query filters accent-insensitively.
  const results = useMemo<ConcelhoEntry[]>(() => searchConcelhos(query, 40), [query])

  const choose = useCallback(
    (dico: string) => {
      onSelect(dico)
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
        <Text style={[styles.title, { color: c.text }]}>Concelho</Text>
        <BottomSheetTextInput
          value={query}
          onChangeText={setQuery}
          placeholder="Pesquisar concelho"
          placeholderTextColor={c.textSecondary}
          autoCorrect={false}
          autoCapitalize="none"
          style={[
            styles.search,
            { backgroundColor: c.backgroundElement, color: c.text },
          ]}
        />
      </View>
      <BottomSheetFlatList
        data={results}
        keyExtractor={(item) => item.dico}
        keyboardShouldPersistTaps="handled"
        contentContainerStyle={styles.list}
        ListEmptyComponent={
          <Text style={[styles.empty, { color: c.textSecondary }]}>
            Sem concelhos para esta pesquisa.
          </Text>
        }
        renderItem={({ item }) => (
          <Pressable
            onPress={() => choose(item.dico)}
            accessibilityRole="button"
            style={({ pressed }) => [
              styles.row,
              pressed && { backgroundColor: c.backgroundElement },
            ]}
          >
            <Text style={[styles.rowLabel, { color: c.text }]}>{item.name}</Text>
            <Text style={[styles.rowDistrict, { color: c.textSecondary }]}>
              {item.district}
            </Text>
          </Pressable>
        )}
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
  empty: {
    fontSize: 14,
    textAlign: 'center',
    paddingVertical: Spacing.four,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.two,
    paddingVertical: Spacing.three,
  },
  rowLabel: {
    fontSize: 16,
    fontWeight: '500',
    flexShrink: 1,
  },
  rowDistrict: {
    fontSize: 13,
  },
})
