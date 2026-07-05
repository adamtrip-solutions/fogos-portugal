# FogosPortugal — notification system plan

Goal: evolve the current notification machinery into one coherent system that serves the web app
today and the Expo mobile app next, with area-based push as the flagship capability.

## 1. What exists today (inventory)

| Piece | State |
|---|---|
| FCM sender (`FcmNotifier`/`FcmSender`) | Working; topic + condition sends, Off/DryRun/On modes, `SendToTokenAsync` for direct-to-device |
| District topics (`FcmTopics`) | Legacy broadcast model (`NewFire(dico, district)` etc.), env-prefixed |
| Delayed dispatcher | 3-min debounce pump (Redis sorted set) used for push debouncing |
| `alert_subscriptions` | Anonymous, Concelho or Point+radius, optional `riskThreshold`, optional `fcmToken` |
| `alert_events` + `alertEvents` query | Poll-based in-app delivery (web uses this: 60s poll → toasts), 7-day TTL, dedupe-key unique index |
| Matching | `AlertMatchHandler` (NEW_INCIDENT / ESCALATION / REKINDLE), `RiskAlertHandler` (RISK) |
| Webhooks | HMAC-signed, per-client, working |

The pieces are right; what's missing for mobile is a **device model** and **delivery-channel
maturity** (token lifecycle, collapse behavior, preferences).

## 2. Target architecture

```
                         ┌── in-app poll (web today) ── alertEvents query
events ──► matchers ──►  ├── FCM push (mobile, web later) ── via devices registry
(created/escalating/     ├── webhooks (API clients)
 rekindle/risk/…)        └── (future) email digest
```

One subscription model, N delivery channels per subscription. The matcher/dedupe layer is already
channel-agnostic — keep it that way.

### 2.1 Device registry (the key new piece)

Problem: FCM tokens rotate; storing a raw token on each subscription (today's model) breaks
silently on rotation and can't be pruned on FCM "unregistered" errors.

New collection `devices`:
```
{ _id, platform: ios|android|web, fcmToken, locale, appVersion,
  createdAt, lastSeenAt, disabled: bool, failureCount }
```
- GraphQL: `registerDevice(platform, fcmToken, locale, appVersion): Device!` (returns deviceId the
  app persists), `refreshDeviceToken(deviceId, fcmToken)`, `deleteDevice(deviceId)`. Anonymous +
  rate-limited like `createAlertSubscription`.
- `alert_subscriptions` gains `DeviceId?` (the `FcmToken` field is deprecated in place: matcher
  resolves device → token at send time). Token rotation = one `refreshDeviceToken` call, every
  subscription follows automatically.
- Pruning: FCM `UNREGISTERED`/`INVALID_ARGUMENT` responses increment `failureCount`; disable the
  device at N=5; a weekly job purges devices disabled or unseen for 120 days (cascade-delete their
  subscriptions).

### 2.2 Delivery semantics

- **Collapse per incident**: pushes about the same incident use FCM `collapse_key` (Android) /
  `apns-collapse-id` (iOS) = incidentId, so a phone shows one evolving notification per fire, not a
  stack. Payload carries the latest state.
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

### 2.4 Broadcast tier (keep, refit)

The district-topic broadcasts (big-fire pushes to whole districts) remain for the legacy audience
and as a zero-setup default: the mobile onboarding offers "notificações para o meu distrito"
(topic subscribe — no server subscription needed) and "alertas personalizados" (device +
subscription model above). National tier: a `national-big` topic for fires crossing 100 assets
(one push per incident via SocialThread-style claim).

### 2.5 Web push (later phase)

The web app's polling works but only while open. Once mobile ships, reuse the same device registry
with `platform: web` (FCM web SDK + VAPID key). The AlertsPopover upgrades from "polling only" to
"polling + optional browser push". No server model changes needed — that's the payoff of the
device registry.

## 3. Backend work packages (when implementation starts)

1. **N1 — device registry**: collection, class map, indexes, 3 mutations, matcher resolves
   DeviceId→token, failure-count pruning + purge job. (Existing FcmToken subscriptions keep working
   through a compatibility read until migrated.)
2. **N2 — delivery semantics**: collapse keys, ESCALATION debounce via delayed dispatcher, deep-link
   payload fields, per-kind/minAssets preference filtering in matchers.
3. **N3 — broadcast refit**: onboarding topics, national tier, DryRun soak against demo data.
4. **N4 — web push**: VAPID/Firebase web config, AlertsPopover upgrade.

Each package follows the established conventions (options POCOs, EventSerializer registration for
any new events, integration tests against the harness, PT copy).

## 4. Open questions

1. Firebase project: reuse the existing one (the FCM config already in place) for the mobile app's
   iOS/Android senders? (Simplest: yes — one project, three app registrations.)
2. iOS: Apple Developer account availability (needed for APNs key + store).
3. Do we want risk-level daily digests (one morning push "risco máximo hoje no seu concelho") as
   part of N2, or keep RISK event-driven only?
