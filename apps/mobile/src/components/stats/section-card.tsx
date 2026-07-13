import type { ReactNode } from 'react'
import { StyleSheet, Text, View } from 'react-native'

import { Spacing } from '@/constants/theme'
import type { ThemeColors } from '@/components/incident/section'

/**
 * A titled dashboard card — mobile port of web's `SectionCard`. A bold title and
 * muted subtitle over its content, on a rounded surface. pt-PT copy is passed
 * verbatim by the caller.
 */
export function SectionCard({
  title,
  subtitle,
  c,
  children,
}: {
  title: string
  subtitle: string
  c: ThemeColors
  children: ReactNode
}) {
  return (
    <View style={[styles.card, { backgroundColor: c.backgroundElement }]}>
      <Text style={[styles.title, { color: c.text }]}>{title}</Text>
      <Text style={[styles.subtitle, { color: c.textSecondary }]}>{subtitle}</Text>
      <View style={styles.body}>{children}</View>
    </View>
  )
}

const styles = StyleSheet.create({
  card: {
    borderRadius: Spacing.three,
    padding: Spacing.three,
  },
  title: {
    fontSize: 16,
    fontWeight: '600',
  },
  subtitle: {
    marginTop: 1,
    fontSize: 13,
    lineHeight: 18,
  },
  body: {
    marginTop: Spacing.three,
  },
})
