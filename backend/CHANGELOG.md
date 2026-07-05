# Changelog

## [1.0.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/api-v0.1.0...api-v1.0.0) (2026-07-05)


### ⚠ BREAKING CHANGES

* **api:** the GraphQL `alertEvents(subscriptionId, after)` query and the `AlertEvent` type are removed from the API schema. All social-media publishing (Twitter/Telegram/Facebook/ Discord) and its configuration keys (Publishing__Channels__{twitter,telegram,facebook, discordPosts}, Twitter__*/Telegram__*/Facebook__*/DiscordPosts__*, Renderer__*) are gone; only Publishing__Channels__fcm remains. The renderer image is no longer built or deployed.
* the .NET solution now lives under backend/ instead of the repo root; local/CI commands must run from backend/.

### Features

* **api:** remove social posting, the renderer sidecar, and the alertEvents poll query ([718e9a2](https://github.com/adamtrip-solutions/fogos-portugal/commit/718e9a2d370ba8c0ca8c5192be0856d0e659e8bb))


### Build System

* move .NET solution into backend/ ([08e07ef](https://github.com/adamtrip-solutions/fogos-portugal/commit/08e07efbc335bb4d6ecab446488db1f6d0c7436d))
