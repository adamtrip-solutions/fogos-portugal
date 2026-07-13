import type { ReactNode } from 'react'
import { StyleSheet, Text, View } from 'react-native'

import { Colors, Spacing } from '@/constants/theme'

/** Widened theme-color bag (either scheme fits), passed down to every section. */
export type ThemeColors = { [K in keyof typeof Colors.light]: string }

/**
 * One labelled block of the incident sheet — an uppercase, tracked section
 * heading (web's `SectionTitle`) over its content. pt-PT heading text is passed
 * verbatim by the caller.
 */
export function Section({
  title,
  c,
  children,
}: {
  title: string
  c: ThemeColors
  children: ReactNode
}) {
  return (
    <View style={styles.section}>
      <Text style={[styles.title, { color: c.textSecondary }]}>{title}</Text>
      {children}
    </View>
  )
}

const styles = StyleSheet.create({
  section: {
    gap: Spacing.two,
  },
  title: {
    fontSize: 12,
    fontWeight: '600',
    textTransform: 'uppercase',
    letterSpacing: 0.8,
  },
})
