# FogosPortugal mobile — web-parity implementation plan

Feature-by-feature plan to bring the mobile app (apps/mobile, Expo + MapLibre RN) to parity with
the web app, plus the mobile-only capabilities. This refines and **supersedes the M0–M3 sequencing**
in `mobile-app-plan.md` (stack decisions there still stand). Reviewed by an independent Claude pass
and a Codex (gpt-5.6-sol) pass; their accepted findings are folded in below.

Where mobile stands today: one screen — live map polling `activeIncidents` every 60 s, basic
bottom-sheet (status badge, title, location, relative time, resource tiles). Everything else below
is new.

In-flight PRs this plan builds on:
- **#69 `feat/app-device-credentials`** — `registerAppDevice → {deviceId, deviceSecret}`,
  `X-Device-Key: fdv1.{id}.{secret}`, App tier (240 req/min, cost budget 20 000, and a cap of
  **2 concurrent GraphQL WebSocket subscriptions** — note: that is a socket cap, unrelated to alert
  subscriptions), device-owned alert subscriptions. **A phase-1 prerequisite, not an upgrade** —
  see F4. Needs two amendments before mobile ships on it (§4): purge-job safety and a per-device
  alert-subscription cap.
- **#67 `feat/fire-danger-layer`** — EFFIS FWI (`mf010.fwi`) raster layer on the web map; CORS `*`,
  so native loads it identically.

## 1. Feature disposition

**Port (phases 1–3):** recent+finished fires merge with the 3 h display window · status-bucket +
age filters + legend · full incident detail (resources incl. heli/plane split, resource-history
chart, nearest-station weather, ICNF cause/burn area, signal badges + reasons, status timeline with
synthetic "Alerta" origin, response times, aircraft, photo grid — display of moderated photos only) ·
hotspot spread scrubber + perimeter KML versions + photo pins · Ocorrências list · Situação ·
Concelho profile · Risco choropleth · Estatísticas · Avisos · weather layers (EFFIS FWI, RainViewer
radar, IPMA rasters) · Sobre/Créditos · deep links + share.

**Mobile-first new (phase 4):** native push alerts (Expo Push).

**Skip:** photo *submission* (cut by decision — iOS `expo-image-picker` camera output carries no
GPS EXIF, so the backend's GPS-required contract is unsatisfiable without a new
location-attach upload contract, and in-app UGC submission triggers Apple's report/block/contact
obligations; revisit post-v1 with a device-authenticated `location`-parameter upload) ·
Conta/Clerk (developer/desktop feature) · API docs page · PWA/SEO/sitemap · web-push flow
(replaced by native push) · wind particles (weatherlayers-gl is WebGL/deck.gl, web-only).

**Realtime:** mobile ships on 60 s polling. The App tier allows 2 concurrent ws subscriptions, but
`SubscriptionSessionInterceptor` only resolves `Bearer`/`apiKey` from the graphql-ws
`connection_init` payload — device callers would connect as Anonymous (cap 0) and be rejected. A
later backend follow-up (accept a `deviceKey` connection parameter, resolve via `DeviceKeyResolver`,
integration-tested) unlocks `activeIncidentsChanged` deltas as an optimization. Not a blocker.

## 2. Cross-cutting foundations (start of phase 1)

- **F0 — SDK alignment (resolved).** The app stays on **Expo SDK 56** (decision: more stable than
  57); `apps/mobile/AGENTS.md` now points at the v56 docs.
- **F1 — TanStack Query + offline.** RN wiring web gets for free: `focusManager` ↔ `AppState`,
  `onlineManager` ↔ `@react-native-community/netinfo`, poll suspension when backgrounded. Query
  keys/staleness mirror web (60 s map feeds, 5 min screens, 30 min risk). **Offline behavior is a
  requirement, not a nicety** (emergency-information app): persist the map/detail/warnings query
  caches to AsyncStorage (`@tanstack/react-query-persist-client`), show a prominent "atualizado há
  X" timestamp + offline banner when serving persisted data, refetch immediately on
  reconnect/foreground, never present stale data as live. Offline map tile packs stay out of v1.
- **F2 — shared workspace packages** (`@fogos/api-client`: GraphQL documents, TS types, filter
  builders, KML→GeoJSON, hotspot helpers, stats helpers; `@fogos/ui-tokens`: status
  buckets/colors, pt-PT labels, formatters). Logic only, no UI components. Hermes note: modern
  Hermes ships `Intl`, so `DateTimeFormat`/`NumberFormat` with `pt-PT` are expected to work — keep
  the existing hand-rolled relative-time formatter (the one real gap already hit) and verify the
  specific formats once on a release-build Android device.
- **F3 — navigation** (lands in phase 2.1, not 1.1): expo-router tabs — **Mapa / Ocorrências /
  Estatísticas / Mais** (stack under Mais: Situação, Risco, Avisos, Concelho/[dico], Alertas,
  Sobre, Créditos). Phase 1 stays a single map stack; in-app links to not-yet-built routes stay
  hidden until their phase lands. Alertas can promote to a tab in phase 4.
- **F4 — device credentials are a phase-1 prerequisite.** The full map poll costs ~236 GraphQL
  cost/min per phone (7 × `Recent` @30 + `Active` @15 + open `Detail` @11) against an anonymous
  budget of 500 *per IP* — three phones behind one carrier CGNAT would throttle each other. So:
  merge #69 first; on first launch the app silently calls `registerAppDevice`, stores the secret
  in `expo-secure-store` (Keychain/Keystore), and sends `X-Device-Key` on every request — each
  phone gets its own 240 req/min bucket. `DEVICE_UNAUTHENTICATED` 401 → wipe + re-register.
  Anonymous is only a degraded fallback (registration outage): reduced poll rate, no alerts.
  Registration abuse is covered by #69's existing IP gate.
- **F5 — deep links.** `fogosportugal://incident/{id}` + Universal/App Links for
  `fogosportugal.pt/?incident=…`. Full scope (not just the scheme): `ios.associatedDomains` +
  Android `intentFilters` in app config, AASA + `assetlinks.json` hosted on the domain,
  query-param→route mapping for `/?incident=…`, and cold-start handling (link opens app from
  killed state). Acceptance criteria for 1.4.

New dependencies (none installed yet): `@tanstack/react-query` (+ persist-client +
`@react-native-async-storage/async-storage`), `@react-native-community/netinfo`,
`expo-secure-store`, `victory-native` **+ its required `@shopify/react-native-skia`** (heavy native
dep — budget a dev-client rebuild), `@shopify/flash-list`; phase 4: `expo-notifications`,
`expo-location`.

## 3. Phases (each item ≈ one PR)

Phases 1–2 (app) and phase 4.0 (backend push work) run **in parallel** — the push backend is the
long pole and shouldn't wait for phase 3.

### Phase 1 — map + incident parity (single map stack, no tabs yet)
1. **1.1 Foundations** — F0, F1, F2, F4; move mobile `api.ts`/`types.ts`/`format.ts` onto the
   shared packages; web adopts the same packages behind its server fns.
2. **1.2 Feeds + filters** — merge `activeIncidents` with the two `Recent` feeds
   (`updatedAfter` now−3 d, 5 pages; + `statusCodes:[7,9]` tail, 2 pages), 3 h finished-fire
   display window; filter control (bucket multi-select + tudo/1h/3h/6h/12h age) and legend as a
   small sheet; "só ativos" shortcut.
3. **1.3 Full incident sheet** — switch selection to the `Detail` query (60 s refetch); sections:
   signal badges, resource tiles + heli/coord/plane rows, resource-history chart (victory-native),
   weather, ICNF, status timeline, response times, aircraft, photo grid (expo-image; read-only
   display of moderated photos). Snap points ~45 % / ~92 %. Concelho tap navigation ships with 2.5.
4. **1.4 Deep links + share** — F5 in full (associated domains, AASA/assetlinks, cold start),
   share sheet on the incident header; deep-linked fire outside the loaded window falls back to
   `fetchIncident(id)` like web.

### Phase 2 — screens
1. **2.1 Tabs scaffold (F3) + Sobre/Créditos** (content screens double as the
   store-review-required about/disclaimer pages).
2. **2.2 Ocorrências** — FlashList infinite list on `incidents(filter, first:50, after)`;
   window/bucket/district filters in a header sheet (router params mirror web's URL params);
   totalCount footer; flags (importante/escalada/reacendimento/críticas). Islands surface via the
   district filter, same as web.
3. **2.3 Estatísticas** — single `Season($year,$prevYear)` query (calculated cost 13 — no budget
   concern); 5 stat tiles, 4 charts (ignitions YoY, cumulative burn area, causes, hourly),
   false-alarm table, response-time medians.
4. **2.4 Situação + Avisos** — situationReports(14) hero tiles with delta chips, narrative, top
   incidents → map deep link, archive; weatherWarnings grouped by district, severity-sorted.
5. **2.5 Risco + Concelho** — `fireRisk(day)` GeoJSON choropleth (`<Layer type="fill">`, taps via
   `GeoJSONSource.onPress`), Hoje/Amanhã/Depois segmented control, legend, concelho search;
   `concelhoProfile(dico)` screen: 5-day risk strip, YoY tiles, active incidents, IPMA warnings.
   Enables the concelho links from 1.3/2.2.

### Phase 3 — map overlays
1. **3.1 Propagação + Perímetro + photo pins** — hotspot scrubber implemented as a numeric
   `acquiredAtMs` threshold on the `Layer` `filter` prop, updated at a bounded rate by the
   scrubber/play animation (MapLibre RN v11 has no imperative `setFilter` and feature-state is
   unreliable — prop-driven filter only); play button (6 s sweep), recency fade, wind chip;
   perimeter version pills → `GET /v3/incidents/{id}/kml-versions/{versionId}` (immutable, cache
   forever) → shared KML→GeoJSON → orange fill; geotagged photos as camera pins.
2. **3.2 Weather layers** — layer control with: EFFIS FWI (direct, day selector, 6-class legend —
   port of #67), RainViewer radar (direct, frame animation), IPMA `lsasaf.risk` (direct — no run
   discovery needed). For the AROME layers (temperature/wind/gust/humidity) the dependency is
   **run discovery, not tile proxying** (native needs no CORS proxy): add a small cached backend
   endpoint returning `{referenceTime, time, regions}` and let MapLibre fetch IPMA tiles directly;
   if that endpoint is deferred, ship v1 with FWI + radar + lsasaf.risk only. Legends + credits
   per layer.

### Phase 4 — Alertas (native push)
Backend first (separate PRs; runs in parallel with app phases 1–2). Scope = notifications-plan
N1 + N2, shrunk by #69 and amended by review:
1. **4.0a Device push binding** — `setMyDevicePushToken(expoPushToken)` /
   `clearMyDevicePushToken()`: device derived from the authenticated `X-Device-Key` caller
   (`FogosCaller.DeviceId`) — **never a client-supplied deviceId** (confused-deputy). Token set
   validates the Expo token format, resets `Disabled`/`FailureCount`, updates `LastSeenAt`, sparse
   unique index on token. `ExpoPushClient` (batched ≤100/req) + receipts job
   (`DeviceNotRegistered` → failureCount → disable at 3) + purge job. **One phone = one device**:
   the #69 `registerAppDevice` identity is the same device the push token attaches to.
2. **4.0b Delivery semantics (N2)** — deep-link payload (`incidentId`/`appRoute`/`webUrl`),
   Android `channelId`s, per-subscription `kinds` + `minAssets` (label it "meios terrestres e
   aéreos" — backend `TotalAssets` = aerial + terrain, not personnel), **escalation debounce**
   (≤1 escalation push per incident per 10 min — reintroduce the delayed-dispatch primitive per
   notifications-plan), and **per-device delivery coalescing** by `(deviceId, dedupeKey)` so
   overlapping subscriptions (concelho + radius over the same fire) produce one notification.
3. **4.1 App: onboarding + registration** — `expo-notifications` config plugin; **create Android
   notification channels before requesting permission** (Android 13+ `POST_NOTIFICATIONS`; iOS
   needs no purpose string for notifications); obtain Expo push token (`getExpoPushTokenAsync`
   with projectId) → `setMyDevicePushToken`; rotation listener converts native tokens via
   `getExpoPushTokenAsync({ devicePushToken })` before re-sending; EAS push credentials.
4. **4.2 App: subscriptions UI** — device-owned subscriptions: concelho picker (+ risk threshold
   4/5) and point+radius (one-shot `expo-location` with iOS location purpose string), per-kind
   toggles + min-assets (needs 4.0b); notification tap → incident screen, including the
   cold-start path (`getLastNotificationResponse`). Promote Alertas in the nav.
5. **4.3 (optional, N3)** — "o meu distrito" one-tap onboarding needs the District subscription
   kind on the backend; out of 4.2's critical path.

### Phase 5 — release engineering + store
1. **5.1 Versioning + release automation** — `app.config.ts` reads the version from
   `apps/mobile/package.json` (it currently hardcodes 1.0.0); add the `mobile` component to
   release-please config + manifest; EAS remote auto-increment for build numbers. **OTA-vs-native
   detection in CI**: runtime policy stays `fingerprint`; on mobile release, CI runs
   `@expo/fingerprint` against the latest production build's fingerprint — match → `eas update`
   to production channel; mismatch → `eas build` + `eas submit`. The decision is mechanical, not
   a human judgment call.
2. **5.2 Store submission checklist** — icons/splash/screenshots; privacy policy + support URLs
   (public pages); App Privacy / Play Data Safety declarations (approximate: push identifiers,
   coarse usage; precise location only if 4.2's point+radius ships in the same build); age
   rating; review notes; TestFlight/internal track → staged promotion (eas submit delivers to
   TestFlight, not straight to review). Decide `supportsTablet: false` for v1 or budget iPad
   layouts + screenshots (current config opts into iPad).

## 4. Backend deltas (complete list)
- **#69 amendments (pre-merge or fast-follow):**
  - Purge safety: `X-Device-Key` auth touches device `LastSeenAt` (throttled, ≤1×/day) so
    `DevicePurgeJob` never deletes an active device; exempt subscriptions with a `DeviceId` from
    the anonymous-subscription purge (`AlertSubscriptionPurgeJob`) — device lifecycle owns the
    cascade.
  - `MaxAlertSubscriptionsPerDevice` (10, mirroring the per-user cap) + integration test — device
    alert subscriptions are currently unbounded.
- Phase 4.0a/4.0b above (push-token mutations on the caller's device, ExpoPushClient + receipts,
  N2 payload/kinds/minAssets, escalation debounce, per-device dedupe).
- Cached AROME run-discovery endpoint `{referenceTime, time, regions}` (phase 3.2; deferrable).
- Optional (realtime, later): `deviceKey` connection parameter in `SubscriptionSessionInterceptor`.
- Nothing else — every screen consumes the existing anonymous-friendly GraphQL/REST surface.

## 5. Risks
- **victory-native → Skia** native dependency: new dev-client build required; validate early in 1.3.
- **#69 merge timing** gates phase-1 public distribution (dev/TestFlight builds can run anonymous).
- **IPMA/EFFIS direct traffic** from devices: keep layer allowlists client-side; the run-discovery
  endpoint centralizes the fragile part; be ready to add a tile passthrough only if upstream blocks
  app traffic.
- **Expo push receipts discipline**: skipping the receipts job silently accumulates dead tokens —
  it ships with 4.0a, not later.
