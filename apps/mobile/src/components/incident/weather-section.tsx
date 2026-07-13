import { StyleSheet, Text, View } from 'react-native'

import type { IncidentWeather } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

/**
 * Nearest-station weather card (web's `WeatherSection`) — temperature, humidity,
 * and wind readings over the station name + distance. Any sensor can be null;
 * renders nothing when every reading is missing.
 */
export function WeatherSection({
  weather,
  c,
}: {
  weather: IncidentWeather
  c: ThemeColors
}) {
  const wind =
    weather.windSpeedKmh != null
      ? `${Math.round(weather.windSpeedKmh)} km/h${
          weather.windDirection ? ` ${weather.windDirection}` : ''
        }`
      : null

  const items = [
    {
      label: 'Temperatura',
      value:
        weather.temperature != null ? `${Math.round(weather.temperature)}°` : null,
    },
    {
      label: 'Humidade',
      value: weather.humidity != null ? `${Math.round(weather.humidity)}%` : null,
    },
    { label: 'Vento', value: wind },
  ].filter((i) => i.value != null)

  if (items.length === 0) return null

  return (
    <Section title="Meteorologia" c={c}>
      <View style={styles.row}>
        {items.map((i) => (
          <View key={i.label} style={styles.item}>
            <Text style={[styles.value, { color: c.text }]}>{i.value}</Text>
            <Text style={[styles.label, { color: c.textSecondary }]}>
              {i.label}
            </Text>
          </View>
        ))}
      </View>
      <Text style={[styles.station, { color: c.textSecondary }]}>
        Estação {weather.stationName} · {weather.distanceKm.toFixed(1)} km
      </Text>
    </Section>
  )
}

const styles = StyleSheet.create({
  row: {
    flexDirection: 'row',
    gap: Spacing.four,
  },
  item: {
    gap: 1,
  },
  value: {
    fontSize: 18,
    fontWeight: '700',
    fontVariant: ['tabular-nums'],
  },
  label: {
    fontSize: 12,
  },
  station: {
    fontSize: 12,
  },
})
