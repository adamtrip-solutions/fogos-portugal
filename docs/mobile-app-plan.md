# FogosPortugal — mobile app plan (Expo + React Native)

Goal: a native app with feature parity with the web app plus the capabilities only mobile can do
well — push notifications for chosen areas, camera-based photo submission, share/deep-link
integration.

## 1. Stack decisions

| Concern | Decision | Why |
|---|---|---|
| Framework | Expo (latest SDK), TypeScript, expo-router | File-based routing like the web app; EAS ecosystem; OTA updates |
| Map | `@maplibre/maplibre-react-native` | Same MapLibre engine + CARTO styles as web → visual parity, same layer mental model (GeoJSON sources, circle/symbol/fill layers) |
| Data | Same GraphQL API (`api.fogosportugal.pt`) + TanStack Query | Native apps have no CORS constraint; direct calls, no server-fn indirection. Query keys/patterns mirror web |
| Shared code | `@fogos/api-client` + `@fogos/ui-tokens` workspace packages | GraphQL documents, TS types, status colors/buckets, PT labels, formatters — logic only, never UI components (DOM ≠ RN) |
| Incident panel | `@gorhom/bottom-sheet` | The web app's mobile drawer UX, native-grade |
| Charts | `victory-native` (XL) | Resource history + stats charts on RN |
| Push | `expo-notifications` + **Expo Push service** (backend batches up to 100 msgs/request, receipts-based pruning) | Pairs with the device registry in the notifications plan; FCM/APNs credentials live in EAS, backend never touches Firebase |
| Builds | EAS Build (dev/preview/production profiles) | No local Xcode/AndroidStudio ceremony; CI-triggerable |
| OTA | EAS Update, channel per environment | JS-only fixes ship in minutes without store review |

## 2. App structure (expo-router)

```
apps/mobile/
├── app/
│   ├── (tabs)/
│   │   ├── index.tsx          # Mapa (full-screen map, bottom-sheet incident panel)
│   │   ├── estatisticas.tsx   # season dashboard
│   │   └── alertas.tsx        # notification/subscription management
│   ├── incident/[id].tsx      # deep-link target (fogosportugal://incident/{id})
│   ├── concelho/[dico].tsx
│   └── sobre.tsx / creditos.tsx / api.tsx   # content screens (store review needs these)
├── src/ (components, hooks, lib — mirrors web conventions)
└── app.json / eas.json
```

Deep links: scheme `fogosportugal://` + Universal/App Links for `fogosportugal.pt/?incident=…`
(same URLs the web pushes/shares — one link works everywhere).

## 3. Feature phases

**M0 — skeleton + read-only parity core**
Map with incident markers (status buckets, escalating pulse), bottom-sheet incident detail
(status, means, resource chart, weather, ICNF, status timeline, signal badges), active-only filter,
dark mode following system. Ship internally via EAS preview + TestFlight/internal track.

**M1 — push notifications (the reason the app exists)**
Onboarding: "o meu distrito" (topic subscribe, zero friction) and/or custom areas (concelho picker
or map point+radius → `registerDevice` + `createAlertSubscription` with DeviceId). Preferences
screen: per-subscription kind toggles, min-assets slider. Notification tap → incident screen.
Requires notifications-plan N1+N2 on the backend first.

**M2 — full parity**
Spread timeline scrubber (hotspots layer animates fine in MapLibre RN), perimeter replay (KML→
GeoJSON already a shared helper), aircraft section, /estatisticas charts, concelho screens,
situation reports feed.

**M3 — mobile-only capabilities + store polish**
Photo submission: camera/gallery → EXIF GPS → existing `POST /v3/incidents/{id}/photos` (this
endpoint was built for exactly this; the web can't do it well). Share sheet on incidents. Widgets
(iOS lock screen "fires near me" / Android) as stretch. Store listing, privacy policy page
(content pages exist), review notes.

## 4. Backend deltas required

- Notifications plan N1/N2 (device registry, preferences, collapse/deep-link payloads) — the only
  hard prerequisite, needed for M1.
- Public API exposure: `api.fogosportugal.pt` reachable + rate limits tuned for app traffic
  (anonymous read QPS will grow); the deployment plan's tunnel handles the transport.
- Nothing else: every screen above consumes the GraphQL/REST surface that already exists.

## 5. CI/CD & releases (fits the monorepo + Blacksmith setup)

- PR CI on Blacksmith: `tsc`, `eslint`, unit tests (jest-expo) — cheap, every PR touching
  `apps/mobile/**` or shared packages.
- EAS builds are NOT run per-PR (slow/credit-consuming): triggered by release only, via
  `eas build --non-interactive` from an Actions job holding `EXPO_TOKEN`.
- Release Please component `mobile` (tag `mobile-vX.Y.Z`): merge of its release PR triggers
  (a) `eas update` to the production channel for JS-only releases, or (b) `eas build` + store
  submission (`eas submit`) when native config changed (`runtimeVersion` policy: `appVersion` —
  bump = new store build required; Release Please bumps it via extra-files).
- Store cadence reality: Apple review takes days — the deploy target for mobile is the store
  pipeline, not the Mac. The Mac/compose stack is untouched by mobile releases.

## 6. Open questions

1. ~~Apple Developer + Google Play accounts~~ **Resolved**: both exist and are linked via Expo/EAS.
2. ~~Firebase project / APNs key~~ **Resolved by the Expo Push decision**: transport credentials
   live in the EAS account; the backend has no Firebase dependency.
3. Background location for "fires near my current location" alerts — v1 says NO (privacy + battery
   + store-review friction); chosen-areas only. Revisit post-launch if users ask.
4. Minimum OS targets (proposal: iOS 16+, Android 8+ — MapLibre RN and Expo defaults).
