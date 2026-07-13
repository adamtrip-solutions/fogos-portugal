import type { ExpoConfig, ConfigContext } from 'expo/config'

// FogosPortugal — Expo app config (CNG: ios/ and android/ are generated, never
// committed). EAS writes `extra.eas.projectId` and `updates.url` in below once
// `eas init` / `eas update:configure` bind the project.
//
// The store icon uses the 1024px master. The smaller Fogos mark remains useful
// for the splash screen and Android adaptive icon foreground.
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
  icon: './assets/images/icon.png',
  ios: {
    supportsTablet: true,
    bundleIdentifier: 'pt.fogosportugal.app',
    appleTeamId: '2A56G82R2N',
    config: {
      usesNonExemptEncryption: false,
    },
    // Universal Links (plan 1.4/F5): opens https://fogosportugal.pt/?incident=…
    // in-app. Requires the AASA file hosted at
    // https://fogosportugal.pt/.well-known/apple-app-site-association.
    associatedDomains: ['applinks:fogosportugal.pt'],
  },
  android: {
    package: 'pt.fogosportugal.app',
    adaptiveIcon: {
      foregroundImage: './assets/images/fogos-icon.png',
      backgroundColor: '#18181b',
    },
    // App Links (plan 1.4/F5): autoVerify against the hosted
    // /.well-known/assetlinks.json so https fogosportugal.pt links open in-app
    // without a chooser. The `fogosportugal://` scheme intent filter is added by
    // expo-router from the top-level `scheme`.
    //
    // Scoped to the home path ONLY (`path: '/'` is Android's exact-match, mirroring
    // the iOS AASA which is already scoped to '/'). The app only handles the root
    // route (`/?incident=…` deep links); other site paths (/ocorrencias, /situacao,
    // /risco, …) must keep opening in the browser, not get captured by the app.
    intentFilters: [
      {
        action: 'VIEW',
        autoVerify: true,
        data: [{ scheme: 'https', host: 'fogosportugal.pt', path: '/' }],
        category: ['BROWSABLE', 'DEFAULT'],
      },
    ],
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
