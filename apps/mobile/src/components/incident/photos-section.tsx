import { Pressable, StyleSheet, View } from 'react-native'
import { Image } from 'expo-image'
import * as WebBrowser from 'expo-web-browser'

import type { IncidentPhoto } from '@fogos/api-client'

import { Spacing } from '@/constants/theme'
import { Section, type ThemeColors } from './section'

/**
 * Moderated-photo grid (web's `PhotosSection`, "Fotografias") — a 3-up grid of
 * approved citizen photos. Tapping one opens its full-resolution `publicUrl` in
 * the in-app browser. Read-only display; no upload UI (cut from the plan).
 */
export function PhotosSection({
  photos,
  c,
}: {
  photos: IncidentPhoto[]
  c: ThemeColors
}) {
  if (photos.length === 0) return null

  return (
    <Section title="Fotografias" c={c}>
      <View style={styles.grid}>
        {photos.map((photo) => (
          <Pressable
            key={photo.id}
            style={styles.cell}
            onPress={() => {
              void WebBrowser.openBrowserAsync(photo.publicUrl)
            }}
            accessibilityRole="imagebutton"
            accessibilityLabel="Abrir fotografia"
          >
            <Image
              source={{ uri: photo.publicUrl }}
              style={[styles.image, { backgroundColor: c.backgroundElement }]}
              contentFit="cover"
              recyclingKey={photo.id}
              transition={150}
            />
          </Pressable>
        ))}
      </View>
    </Section>
  )
}

const styles = StyleSheet.create({
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: Spacing.two,
  },
  cell: {
    width: '31.5%',
  },
  image: {
    width: '100%',
    aspectRatio: 1,
    borderRadius: Spacing.two,
  },
})
