# Changelog

## [2.5.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.4.0...api-v2.5.0) (2026-07-07)


### Features

* **api:** API keys are read-only — central mutation guard ([#64](https://github.com/adamtrip-solutions/fogos-portugal/issues/64)) ([9de0c00](https://github.com/adamtrip-solutions/fogos-portugal/commit/9de0c004becd6a628b905e8f3a65593238f2ceb2))
* automatic IPMA avisos — remove the manual warning channel ([#61](https://github.com/adamtrip-solutions/fogos-portugal/issues/61)) ([a4211e6](https://github.com/adamtrip-solutions/fogos-portugal/commit/a4211e6f17ef8cba9a65c5931c38e8d60fedd84b))

## [2.4.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.3.1...api-v2.4.0) (2026-07-07)


### Features

* **api:** Web Push delivery channel for alerts ([#57](https://github.com/adamtrip-solutions/fogos-portugal/issues/57)) ([40d413a](https://github.com/adamtrip-solutions/fogos-portugal/commit/40d413abc2d0f54d067ec6d9cbdf15d28113a029))

## [2.3.1](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.3.0...api-v2.3.1) (2026-07-07)


### Bug Fixes

* **api:** ship the HotChocolate 16 migration ([#53](https://github.com/adamtrip-solutions/fogos-portugal/issues/53)) ([2b585fe](https://github.com/adamtrip-solutions/fogos-portugal/commit/2b585fecbab41920e2066015d700a2965c23b04e))

## [2.3.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.2.0...api-v2.3.0) (2026-07-06)


### Features

* **api:** self-service API keys and account-owned alert subscriptions ([#48](https://github.com/adamtrip-solutions/fogos-portugal/issues/48)) ([e4da159](https://github.com/adamtrip-solutions/fogos-portugal/commit/e4da159000c8a6af1aeffbb9070c9ffeb5e6eceb))
* Clerk-backed user identity as a second Bearer issuer (accounts 1/3) ([#45](https://github.com/adamtrip-solutions/fogos-portugal/issues/45)) ([5b5fca4](https://github.com/adamtrip-solutions/fogos-portugal/commit/5b5fca4b26cd7df7af017c5e09a2953eb341cb53))


### Bug Fixes

* every incident gets a status timeline from creation ([#46](https://github.com/adamtrip-solutions/fogos-portugal/issues/46)) ([4ae1a35](https://github.com/adamtrip-solutions/fogos-portugal/commit/4ae1a3553a38ef298531bb1e2ecd6497190c9286))
* **worker:** real natureza and canonical DICO for ICNF-created incidents ([#42](https://github.com/adamtrip-solutions/fogos-portugal/issues/42)) ([c73ddcb](https://github.com/adamtrip-solutions/fogos-portugal/commit/c73ddcb464d9c37f2ccbccfbbe16de3a4622b35e))

## [2.2.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.1.0...api-v2.2.0) (2026-07-06)


### Features

* incidents table page + map fire filters ([#34](https://github.com/adamtrip-solutions/fogos-portugal/issues/34)) ([4a9b4c1](https://github.com/adamtrip-solutions/fogos-portugal/commit/4a9b4c1bb65978d27dd2be5455b121d9abb13280))
* map feed keyed on updatedAt (long-running fires stay visible) ([#35](https://github.com/adamtrip-solutions/fogos-portugal/issues/35)) ([ed4aef9](https://github.com/adamtrip-solutions/fogos-portugal/commit/ed4aef934c6812f476676c09ea9d1c82277d2594))


### Bug Fixes

* **api:** relax IcnfClient resilience timeouts for fire-season load ([#32](https://github.com/adamtrip-solutions/fogos-portugal/issues/32)) ([5f2981a](https://github.com/adamtrip-solutions/fogos-portugal/commit/5f2981ab3eeb704020ef9f068872cb5fb180b9ea))

## [2.1.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.0.1...api-v2.1.0) (2026-07-06)


### Features

* **api:** infer incident location from coordinates when concelho lookup misses ([#29](https://github.com/adamtrip-solutions/fogos-portugal/issues/29)) ([4571736](https://github.com/adamtrip-solutions/fogos-portugal/commit/4571736deb7d97e2057ab023819d5c95867774bc))

## [2.0.1](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v2.0.0...api-v2.0.1) (2026-07-06)


### Bug Fixes

* **api:** skip malformed Discord ops webhook URLs instead of erroring per alert ([#25](https://github.com/adamtrip-solutions/fogos-portugal/issues/25)) ([50bc193](https://github.com/adamtrip-solutions/fogos-portugal/commit/50bc193749ae82357d8779b70efbf49cc6abc938))

## [2.0.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v1.0.0...api-v2.0.0) (2026-07-05)


### ⚠ BREAKING CHANGES

* **api:** the `fcmToken` field is removed from the `createAlertSubscription` GraphQL input (CreateAlertSubscriptionInput) and from the AlertSubscription type. Clients must stop sending it; device push delivery returns with the Expo-based N1 device registry.

### Features

* **api:** close out feed-dropped incidents with a real terminal transition ([#13](https://github.com/adamtrip-solutions/fogos-portugal/issues/13)) ([b040112](https://github.com/adamtrip-solutions/fogos-portugal/commit/b040112e182e52315346c78380714395013d86ac))
* **api:** remove FCM/push stack, swap MinIO→R2, rewrite env templates ([#10](https://github.com/adamtrip-solutions/fogos-portugal/issues/10)) ([dc6c713](https://github.com/adamtrip-solutions/fogos-portugal/commit/dc6c713402947c1fba2faaacccbb614ca9c1e4f9))


### Bug Fixes

* **api:** survive invalid Sentry DSN and Auth PEM config ([#14](https://github.com/adamtrip-solutions/fogos-portugal/issues/14)) ([7500e48](https://github.com/adamtrip-solutions/fogos-portugal/commit/7500e48eb8598158e93741df315158c200394ef5))

## [1.0.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v0.1.0...api-v1.0.0) (2026-07-05)


### ⚠ BREAKING CHANGES

* **api:** the GraphQL `alertEvents(subscriptionId, after)` query and the `AlertEvent` type are removed from the API schema. All social-media publishing (Twitter/Telegram/Facebook/ Discord) and its configuration keys (Publishing__Channels__{twitter,telegram,facebook, discordPosts}, Twitter__*/Telegram__*/Facebook__*/DiscordPosts__*, Renderer__*) are gone; only Publishing__Channels__fcm remains. The renderer image is no longer built or deployed.
* the .NET solution now lives under backend/ instead of the repo root; local/CI commands must run from backend/.

### Features

* **api:** remove social posting, the renderer sidecar, and the alertEvents poll query ([718e9a2](https://github.com/adamtrip-solutions/fogos-portugal/commit/718e9a2d370ba8c0ca8c5192be0856d0e659e8bb))


### Build System

* move .NET solution into backend/ ([08e07ef](https://github.com/adamtrip-solutions/fogos-portugal/commit/08e07efbc335bb4d6ecab446488db1f6d0c7436d))
