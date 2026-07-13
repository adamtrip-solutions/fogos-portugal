import { Fragment } from 'react'
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { Link } from 'expo-router'
import type { Href } from 'expo-router'
import { SymbolView } from 'expo-symbols'
import type { SFSymbol } from 'expo-symbols'

import { Colors, Spacing } from '@/constants/theme'

/**
 * "Mais" tab — grouped list of the routes that don't earn a tab. Order mirrors
 * the web drawer's info group (Situação, Avisos, Risco — Alertas lands in phase 4)
 * followed by the content pages (Sobre, Créditos). Situação/Risco/Avisos are
 * placeholders until phases 2.4/2.5; Sobre/Créditos are live.
 */
interface Row {
  href: Href
  label: string
  symbol: SFSymbol
  /** Text glyph shown where SF Symbols are unavailable (Android/web). */
  glyph: string
}

const INFO_ROWS: Row[] = [
  { href: '/situacao', label: 'Situação', symbol: 'waveform.path.ecg', glyph: '∿' },
  { href: '/avisos', label: 'Avisos', symbol: 'exclamationmark.triangle', glyph: '⚠' },
  {
    href: '/risco',
    label: 'Risco',
    symbol: 'gauge.with.dots.needle.bottom.50percent',
    glyph: '◔',
  },
]

const PAGE_ROWS: Row[] = [
  { href: '/sobre', label: 'Sobre o projeto', symbol: 'info.circle', glyph: 'ⓘ' },
  { href: '/creditos', label: 'Créditos e fontes', symbol: 'book.closed', glyph: '❧' },
]

export default function MaisScreen() {
  const c = Colors[useColorScheme() === 'dark' ? 'dark' : 'light']
  const insets = useSafeAreaInsets()

  return (
    <ScrollView
      style={{ backgroundColor: c.background }}
      contentContainerStyle={[
        styles.content,
        { paddingBottom: insets.bottom + Spacing.four },
      ]}
      contentInsetAdjustmentBehavior="automatic"
      showsVerticalScrollIndicator={false}
    >
      <Group rows={INFO_ROWS} />
      <Group rows={PAGE_ROWS} />
    </ScrollView>
  )
}

function Group({ rows }: { rows: Row[] }) {
  const c = Colors[useColorScheme() === 'dark' ? 'dark' : 'light']
  return (
    <View style={[styles.group, { backgroundColor: c.backgroundElement }]}>
      {rows.map((row, i) => (
        <Fragment key={row.href as string}>
          {i > 0 && (
            <View style={[styles.divider, { backgroundColor: c.backgroundSelected }]} />
          )}
          <RowLink row={row} />
        </Fragment>
      ))}
    </View>
  )
}

function RowLink({ row }: { row: Row }) {
  const c = Colors[useColorScheme() === 'dark' ? 'dark' : 'light']
  return (
    <Link href={row.href} asChild>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={row.label}
        style={({ pressed }) => [styles.row, pressed && { backgroundColor: c.backgroundSelected }]}
      >
        <SymbolView
          name={row.symbol}
          size={22}
          tintColor={c.textSecondary}
          fallback={<Text style={[styles.glyph, { color: c.textSecondary }]}>{row.glyph}</Text>}
        />
        <Text style={[styles.label, { color: c.text }]}>{row.label}</Text>
        <SymbolView
          name="chevron.right"
          size={14}
          tintColor={c.textSecondary}
          fallback={<Text style={[styles.chevron, { color: c.textSecondary }]}>›</Text>}
        />
      </Pressable>
    </Link>
  )
}

const styles = StyleSheet.create({
  content: {
    padding: Spacing.four,
    gap: Spacing.four,
  },
  group: {
    borderRadius: Spacing.three,
    overflow: 'hidden',
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: Spacing.three,
    paddingVertical: Spacing.three,
    paddingHorizontal: Spacing.three,
  },
  divider: {
    height: StyleSheet.hairlineWidth,
    marginLeft: Spacing.three + 22 + Spacing.three,
  },
  glyph: {
    fontSize: 18,
    fontWeight: '700',
    width: 22,
    textAlign: 'center',
  },
  label: {
    flex: 1,
    fontSize: 16,
    fontWeight: '500',
  },
  chevron: {
    fontSize: 18,
    fontWeight: '600',
  },
})
