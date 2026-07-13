import { StyleSheet, Text, useColorScheme } from 'react-native'
import type { ColorValue } from 'react-native'
import { Tabs } from 'expo-router'
import { SymbolView } from 'expo-symbols'
import type { SFSymbol } from 'expo-symbols'

import { Colors } from '@/constants/theme'

/** Brand orange — the active tab tint (shared with the map's accent glyphs). */
const ACTIVE = '#FF6E02'

/**
 * Tab icon: SF Symbol on iOS, a monochrome text glyph elsewhere (Android/web),
 * both tinted with the tab's active/inactive color — mirrors the map-pill-button
 * fallback pattern.
 */
function TabIcon({
  symbol,
  glyph,
  color,
}: {
  symbol: SFSymbol
  glyph: string
  color: ColorValue
}) {
  return (
    <SymbolView
      name={symbol}
      size={26}
      tintColor={color}
      fallback={<Text style={[styles.glyph, { color }]}>{glyph}</Text>}
    />
  )
}

/**
 * Tab navigation (plan F3): Mapa / Ocorrências / Estatísticas / Mais. The map tab
 * (`index`) runs headerless and full-bleed; the other tabs get a native header.
 * Theme-aware tab bar via Colors from constants/theme.ts.
 */
export default function TabsLayout() {
  const scheme = useColorScheme() === 'dark' ? 'dark' : 'light'
  const c = Colors[scheme]

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: ACTIVE,
        tabBarInactiveTintColor: c.textSecondary,
        tabBarStyle: {
          backgroundColor: c.background,
          borderTopColor: c.backgroundSelected,
        },
        headerStyle: { backgroundColor: c.background },
        headerTitleStyle: { color: c.text },
        headerTintColor: c.text,
        headerShadowVisible: false,
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Mapa',
          tabBarIcon: ({ color }) => <TabIcon symbol="map" glyph="◈" color={color} />,
        }}
      />
      <Tabs.Screen
        name="ocorrencias"
        options={{
          title: 'Ocorrências',
          headerShown: true,
          tabBarIcon: ({ color }) => (
            <TabIcon symbol="list.bullet" glyph="≡" color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="estatisticas"
        options={{
          title: 'Estatísticas',
          headerShown: true,
          tabBarIcon: ({ color }) => (
            <TabIcon symbol="chart.bar" glyph="▥" color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="mais"
        options={{
          title: 'Mais',
          headerShown: true,
          tabBarIcon: ({ color }) => (
            <TabIcon symbol="ellipsis" glyph="⋯" color={color} />
          ),
        }}
      />
    </Tabs>
  )
}

const styles = StyleSheet.create({
  glyph: {
    fontSize: 22,
    fontWeight: '700',
  },
})
