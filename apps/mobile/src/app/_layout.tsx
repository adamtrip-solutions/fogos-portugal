import { useEffect } from 'react'
import { DarkTheme, DefaultTheme, Stack, ThemeProvider } from 'expo-router'
import { StatusBar } from 'expo-status-bar'
import * as SplashScreen from 'expo-splash-screen'
import { AppState, Platform, useColorScheme, type AppStateStatus } from 'react-native'
import { GestureHandlerRootView } from 'react-native-gesture-handler'
import { SafeAreaProvider } from 'react-native-safe-area-context'
import { BottomSheetModalProvider } from '@gorhom/bottom-sheet'
import { focusManager } from '@tanstack/react-query'
import { PersistQueryClientProvider } from '@tanstack/react-query-persist-client'

import {
  asyncStoragePersister,
  CACHE_MAX_AGE,
  queryClient,
  shouldDehydrateQuery,
} from '@/lib/query'

SplashScreen.preventAutoHideAsync()

// focusManager ↔ AppState: TanStack treats "focused" as the signal to run
// interval polls and refetch-on-focus. Map RN's foreground/background to it so
// polling suspends when backgrounded and resumes (with an immediate refetch) on
// return. `web` keeps TanStack's own window-focus handling.
function onAppStateChange(status: AppStateStatus): void {
  if (Platform.OS !== 'web') {
    focusManager.setFocused(status === 'active')
  }
}

export default function RootLayout() {
  const colorScheme = useColorScheme()

  useEffect(() => {
    // Nothing to preload before the map — hide the splash on first mount.
    void SplashScreen.hideAsync()
  }, [])

  useEffect(() => {
    const subscription = AppState.addEventListener('change', onAppStateChange)
    return () => subscription.remove()
  }, [])

  return (
    <GestureHandlerRootView style={{ flex: 1 }}>
      <SafeAreaProvider>
        <PersistQueryClientProvider
          client={queryClient}
          persistOptions={{
            persister: asyncStoragePersister,
            maxAge: CACHE_MAX_AGE,
            dehydrateOptions: { shouldDehydrateQuery },
          }}
        >
          <ThemeProvider value={colorScheme === 'dark' ? DarkTheme : DefaultTheme}>
            <BottomSheetModalProvider>
              <Stack screenOptions={{ headerShown: false }}>
                {/* The tab group owns the map + its own headers/tab bar. */}
                <Stack.Screen name="(tabs)" />
                {/* Content pages — pushed over the tabs with a native header. */}
                <Stack.Screen name="sobre" options={{ headerShown: true, title: 'Sobre o projeto' }} />
                <Stack.Screen name="creditos" options={{ headerShown: true, title: 'Créditos e fontes' }} />
                {/* Live-context placeholders (phases 2.4/2.5) — same native header. */}
                <Stack.Screen name="situacao" options={{ headerShown: true, title: 'Situação' }} />
                <Stack.Screen name="avisos" options={{ headerShown: true, title: 'Avisos' }} />
                <Stack.Screen name="risco" options={{ headerShown: true, title: 'Risco' }} />
                {/* Concelho profile — native header + back; title follows the
                    concelho name (set in-screen once the profile loads). */}
                <Stack.Screen
                  name="concelho/[dico]"
                  options={{ headerShown: true, title: 'Concelho' }}
                />
                {/* Deep-link redirect target — renders no chrome; it bounces to
                    `/?incident=…` (i.e. into the `(tabs)` map) with the fire
                    selected. Listed so the cold-start initial-URL route is
                    explicit and nothing swallows it. */}
                <Stack.Screen name="incident/[id]" />
              </Stack>
              <StatusBar style="auto" />
            </BottomSheetModalProvider>
          </ThemeProvider>
        </PersistQueryClientProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  )
}
