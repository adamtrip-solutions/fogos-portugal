# FogosPortugal — notification system plan

Goal: evolve the current notification machinery into one coherent system that serves the web app
today and the Expo mobile app next, with area-based push as the flagship capability.

> **Update 2026-07-05 — push-only.** The anonymous in-app polling channel (the `alertEvents` query
> and the web AlertsPopover/toasts) has been removed. `alert_events` remains as the internal
> matcher-dedupe store (write-only, no anonymous read surface). Delivery going forward is push
> (+ webhooks for API clients); N4 web-push stays a future item. Sections below that describe the
> poll channel are retained as historical context.
>
> **Update 2026-07-05 (2) — Expo Push is the delivery layer.** The mobile app will be Expo-based,
> so the backend sends through the **Expo Push service** (batched HTTP API) instead of talking to
> FCM/APNs directly. Firebase disappears from the backend entirely — the FCM server key and APNs
> key live in the user's EAS account as Expo's transport credentials. The FCM-specific details
> below (`FcmNotifier`, topics, collapse keys) describe the current legacy code, which N1 replaces.

## 1. What exists today (inventory)

| Piece | State |
|---|---|
| FCM sender (`FcmNotifier`/`FcmSender`) | Working; topic + condition sends, Off/DryRun/On modes, `SendToTokenAsync` for direct-to-device |
| District topics (`FcmTopics`) | Legacy broadcast model (`NewFire(dico, district)` etc.), env-prefixed |
| Delayed dispatcher | 3-min debounce pump (Redis sorted set) used for push debouncing |
| `alert_subscriptions` | Anonymous, Concelho or Point+radius, optional `riskThreshold`, optional `fcmToken` |
| `alert_events` | Internal matcher-dedupe store (7-day TTL, dedupe-key unique index). The anonymous `alertEvents` poll query was **removed 2026-07-05** — write-only now |
| Matching | `AlertMatchHandler` (NEW_INCIDENT / ESCALATION / REKINDLE), `RiskAlertHandler` (RISK) |
| Webhooks | HMAC-signed, per-client, working |

The pieces are right; what's missing for mobile is a **device model** and **delivery-channel
maturity** (token lifecycle, collapse behavior, preferences).

## 2. Target architecture

```
                         ┌── FCM push (mobile, web later) ── via devices registry
events ──► matchers ──►  ├── webhooks (API clients)
(created/escalating/     └── (future) email digest
 rekindle/risk/…)        (in-app poll / alertEvents query removed 2026-07-05)
```

One subscription model, N delivery channels per subscription. The matcher/dedupe layer is already
channel-agnostic — keep it that way.

### 2.1 Device registry (the key new piece)

Problem: push tokens rotate; storing a raw token on each subscription (today's model) breaks
silently on rotation and can't be pruned on delivery errors.

New collection `devices` (token = **Expo push token**, `ExponentPushToken[…]`, obtained in the app
via `expo-notifications`):
```
{ _id, platform: ios|android, expoPushToken, locale, appVersion,
  createdAt, lastSeenAt, disabled: bool, failureCount }
```
- GraphQL: `registerDevice(platform, expoPushToken, locale, appVersion): Device!` (returns deviceId
  the app persists), `refreshDeviceToken(deviceId, expoPushToken)`, `deleteDevice(deviceId)`.
  Anonymous + rate-limited like `createAlertSubscription`.
- `alert_subscriptions` gains `DeviceId?` (the legacy `FcmToken` field is removed with N1; nothing
  ships against it). Matcher resolves device → token at send time; token rotation = one
  `refreshDeviceToken` call, every subscription follows automatically.
- Delivery: a single `ExpoPushClient` (typed HttpClient) POSTs to the Expo push API in **batches of
  up to 100 messages per request** (Expo's native batching). Sends return tickets; a follow-up job
  polls the **receipts endpoint** (~30 min later) and handles errors: `DeviceNotRegistered` →
  increment `failureCount`, disable the device at N=3; a weekly job purges devices disabled or
  unseen for 120 days (cascade-delete their subscriptions).

### 2.2 Delivery semantics

- **No collapse ids**: the Expo push API does not expose FCM `collapse_key`/`apns-collapse-id`, so
  "one evolving notification per fire" isn't available. Mitigation is debounce-first (below) so
  phones don't stack notifications; Android grouping via `channelId` per alert kind is still
  available client-side.
- **Debounce**: reuse the delayed dispatcher for ESCALATION (means change often) — one push per
  incident per 10 min max. NEW_INCIDENT and REKINDLE send immediately.
- **Dedupe**: existing `alert_events` DedupeKey scheme stays the source of truth for "was this
  subscription already told about X" — the push channel keys off the same insert (push only fires
  when the alert_event insert succeeded, which is exactly once).
- **Deep links**: every push data payload includes `{ incidentId, webUrl:
  "https://fogosportugal.pt/?incident={id}", appRoute: "fogosportugal://incident/{id}" }`. Web
  and mobile route accordingly. Configure Universal Links / App Links on fogosportugal.pt so webUrl
  opens the app when installed.

### 2.3 Subscription preferences (per subscription)

Add to `alert_subscriptions`:
- `Kinds: [NEW_INCIDENT|ESCALATION|REKINDLE|RISK]` — default all; the mobile UI exposes toggles.
- `MinAssets: int?` — "only notify for fires with ≥ N operacionais" (matcher filters NEW_INCIDENT/
  ESCALATION against current TotalAssets).
- (later) `QuietHours: {start,end}?` — suppress non-ESCALATION pushes overnight; ESCALATION in the
  subscriber's area always goes through.

### 2.4 Broadcast tier (refit — no topics)

Expo push has no topic concept, so broadcast tiers become subscription kinds handled by the SAME
matcher as concelho/point (one matching path, no parallel system):
- `District` kind — the mobile onboarding's zero-friction "notificações para o meu distrito".
- `National` kind — big fires only (≥100 assets), one push per incident (claimed once via
  `IProcessedMarker`).
The legacy FCM district-topic code is retired in N1 (no app tokens exist yet, so nothing is lost).

### 2.5 Web push (later phase)

Expo push tokens cover native apps only — browsers are out of scope for the Expo path. If/when web
push happens (N4), it's standard Web Push (VAPID) with `platform: web` devices holding a Web Push
subscription instead of an Expo token. Independent of the mobile work.

## 3. Backend work packages (when implementation starts)

1. **N1 — device registry + Expo delivery**: `devices` collection/class map/indexes, 3 mutations,
   `ExpoPushClient` (100-message batches) + receipts-polling job + pruning/purge, matcher resolves
   DeviceId→token. Retires `FcmSender`/`FcmNotifier`/`FcmTopics` and the subscription `FcmToken`
   field (nothing ships against them).
2. **N2 — delivery semantics**: ESCALATION debounce via delayed dispatcher, deep-link payload
   fields, per-kind/minAssets preference filtering in matchers, Android channelIds per alert kind.
3. **N3 — broadcast kinds**: District + National subscription kinds, DryRun soak against demo data.
4. **N4 — web push** (far future): standard Web Push/VAPID, `platform: web` devices.

Each package follows the established conventions (options POCOs, EventSerializer registration for
any new events, integration tests against the harness, PT copy).

## 4. Open questions

1. ~~Firebase project reuse~~ **Resolved by the Expo decision**: the backend never talks to
   Firebase; FCM/APNs credentials are uploaded to the user's EAS account (already linked) as Expo's
   transport. No Firebase SDK/config in this repo.
2. ~~Apple Developer account~~ **Resolved**: exists, linked via Expo/EAS.
3. Do we want risk-level daily digests (one morning push "risco máximo hoje no seu concelho") as
   part of N2, or keep RISK event-driven only?
