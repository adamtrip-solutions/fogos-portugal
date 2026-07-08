import type { ExpoConfig, ConfigContext } from 'expo/config'

// FogosPortugal — Expo app config (CNG: ios/ and android/ are generated, never
// committed). EAS writes `extra.eas.projectId` and `updates.url` in below once
// `eas init` / `eas update:configure` bind the project.
//
// Icon/splash: sourced from apps/web/public/icon-512.png (copied to
// assets/images/fogos-icon.png). Store submission will need a 1024px master
// icon later — the 512px source is fine for dev/preview builds but must be
// swapped before the first App Store / Play upload.
export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: 'FogosPortugal',
  slug: 'fogos-portugal',
  owner: 'adamtrip-solutions',
  version: '1.0.0',
  orientation: 'portrait',
  scheme: 'fogosportugal',
  userInterfaceStyle: 'automatic',
  // New Architecture is always enabled in SDK 57 — the `newArchEnabled` flag was
  // removed from the config type (there is no legacy arch to opt back into).
  icon: './assets/images/fogos-icon.png',
  ios: {
    supportsTablet: true,
    bundleIdentifier: 'pt.fogosportugal.app',
  },
  android: {
    package: 'pt.fogosportugal.app',
    adaptiveIcon: {
      foregroundImage: './assets/images/fogos-icon.png',
      backgroundColor: '#18181b',
    },
  },
  web: {
    output: 'static',
    favicon: './assets/images/favicon.png',
  },
  plugins: [
    'expo-router',
    [
      'expo-splash-screen',
      {
        backgroundColor: '#18181b',
        image: './assets/images/fogos-icon.png',
        imageWidth: 160,
        dark: {
          backgroundColor: '#18181b',
          image: './assets/images/fogos-icon.png',
          imageWidth: 160,
        },
      },
    ],
    'expo-dev-client',
    'expo-updates',
    'expo-font',
    'expo-image',
    'expo-status-bar',
    'expo-web-browser',
    '@maplibre/maplibre-react-native',
  ],
  experiments: {
    typedRoutes: true,
    reactCompiler: true,
  },
  // OTA updates: fingerprint runtime policy so a native change forces a matching
  // runtime and JS-only fixes ship over-the-air. `updates.url` is filled by
  // `eas update:configure`.
  runtimeVersion: {
    policy: 'fingerprint',
  },
  updates: {
    url: 'https://u.expo.dev/7367ad61-668d-438f-a6d4-c0e06714a53c',
  },
  extra: {
    eas: {
      projectId: '7367ad61-668d-438f-a6d4-c0e06714a53c',
    },
  },
})
