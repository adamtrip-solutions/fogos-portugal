import { useCallback, type ReactNode } from 'react'
import {
  Pressable,
  ScrollView,
  StyleSheet,
  Text,
  View,
  useColorScheme,
} from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { SymbolView } from 'expo-symbols'
import { openBrowserAsync } from 'expo-web-browser'

import { Colors, Spacing } from '@/constants/theme'

/**
 * Reusable scaffold for the static content pages (Sobre, Créditos) — the mobile
 * port of web's `content-page.tsx`. Renders under a native Stack header (title +
 * back), so this only owns the scrolling body: an optional lead paragraph, then
 * a single column of themed cards / tinted callouts. Sits over the tab group.
 */
type Scheme = 'light' | 'dark'

/** Tone → tinted background/border + accent text, resolved per color scheme. */
type Tone = 'neutral' | 'warn' | 'positive' | 'accent'

const TONES: Record<
  Exclude<Tone, 'neutral'>,
  { bg: string; border: string; accent: Record<Scheme, string> }
> = {
  warn: {
    bg: 'rgba(245,158,11,0.09)',
    border: 'rgba(245,158,11,0.30)',
    accent: { light: '#b45309', dark: '#fcd34d' },
  },
  positive: {
    bg: 'rgba(16,185,129,0.09)',
    border: 'rgba(16,185,129,0.28)',
    accent: { light: '#047857', dark: '#6ee7b7' },
  },
  accent: {
    bg: 'rgba(255,110,2,0.09)',
    border: 'rgba(255,110,2,0.32)',
    accent: { light: '#c2410c', dark: '#fdba74' },
  },
}

export function ContentScreen({
  lead,
  children,
}: {
  /** Larger muted intro line (web's `lead`), shown above the cards. */
  lead?: string
  children: ReactNode
}) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const insets = useSafeAreaInsets()

  return (
    <ScrollView
      style={{ backgroundColor: c.background }}
      contentContainerStyle={[
        styles.content,
        { paddingBottom: insets.bottom + Spacing.six },
      ]}
      contentInsetAdjustmentBehavior="automatic"
      showsVerticalScrollIndicator={false}
    >
      {lead != null && (
        <Text style={[styles.lead, { color: c.textSecondary }]}>{lead}</Text>
      )}
      {children}
    </ScrollView>
  )
}

/** A plain themed content card with an optional heading. */
export function Card({
  title,
  children,
}: {
  title?: string
  children: ReactNode
}) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  return (
    <View style={[styles.card, { backgroundColor: c.backgroundElement }]}>
      {title != null && (
        <Text style={[styles.cardTitle, { color: c.text }]}>{title}</Text>
      )}
      {children}
    </View>
  )
}

/** A tinted callout (disclaimer, commitment, dedication) with an accent title. */
export function Callout({
  tone,
  title,
  children,
}: {
  tone: Exclude<Tone, 'neutral'>
  title: string
  children: ReactNode
}) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const t = TONES[tone]
  return (
    <View style={[styles.card, { backgroundColor: t.bg, borderColor: t.border, borderWidth: 1 }]}>
      <Text style={[styles.cardTitle, { color: t.accent[scheme] }]}>{title}</Text>
      {children}
    </View>
  )
}

/** Body paragraph. Nest <Strong> / <Link> inside for inline emphasis + links. */
export function Paragraph({ children }: { children: ReactNode }) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  return <Text style={[styles.paragraph, { color: c.textSecondary }]}>{children}</Text>
}

/** Inline bold — for the emphasised "112" and similar. */
export function Strong({ children }: { children: ReactNode }) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  return <Text style={[styles.strong, { color: c.text }]}>{children}</Text>
}

/** Inline external link (flows within a Paragraph); opens in the in-app browser. */
export function Link({ href, children }: { href: string; children: ReactNode }) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const accent = scheme === 'dark' ? '#fdba74' : '#c2410c'
  const onPress = useCallback(() => void openBrowserAsync(href), [href])
  return (
    <Text style={[styles.link, { color: accent }]} onPress={onPress}>
      {children}
    </Text>
  )
}

/** A tappable source row (name + description + external-link glyph). */
export function SourceRow({
  name,
  desc,
  href,
  first,
}: {
  name: string
  desc: string
  href: string
  /** Omit the top hairline for the first row in a list. */
  first?: boolean
}) {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]
  const onPress = useCallback(() => void openBrowserAsync(href), [href])
  return (
    <Pressable
      accessibilityRole="link"
      accessibilityLabel={name}
      onPress={onPress}
      style={({ pressed }) => [
        styles.sourceRow,
        !first && { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: c.backgroundSelected },
        pressed && { opacity: 0.6 },
      ]}
    >
      <View style={styles.sourceText}>
        <Text style={[styles.sourceName, { color: c.text }]}>{name}</Text>
        <Text style={[styles.sourceDesc, { color: c.textSecondary }]}>{desc}</Text>
      </View>
      <SymbolView
        name="arrow.up.right"
        size={15}
        tintColor={c.textSecondary}
        fallback={<Text style={[styles.sourceGlyph, { color: c.textSecondary }]}>↗</Text>}
      />
    </Pressable>
  )
}

const styles = StyleSheet.create({
  content: {
    paddingHorizontal: Spacing.four,
    paddingTop: Spacing.three,
    gap: Spacing.four,
  },
  lead: {
    fontSize: 16,
    lineHeight: 24,
  },
  card: {
    borderRadius: Spacing.three,
    padding: Spacing.four,
    gap: Spacing.three,
  },
  cardTitle: {
    fontSize: 17,
    fontWeight: '700',
  },
  paragraph: {
    fontSize: 15,
    lineHeight: 22,
  },
  strong: {
    fontWeight: '700',
  },
  link: {
    fontWeight: '600',
    textDecorationLine: 'underline',
  },
  sourceRow: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: Spacing.three,
    paddingVertical: Spacing.three,
  },
  sourceText: {
    flex: 1,
    gap: 2,
  },
  sourceName: {
    fontSize: 15,
    fontWeight: '600',
  },
  sourceDesc: {
    fontSize: 13,
    lineHeight: 18,
  },
  sourceGlyph: {
    fontSize: 15,
    fontWeight: '700',
  },
})
