# Changelog

## [1.1.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.0.0...web-v1.1.0) (2026-07-05)


### Features

* **api:** close out feed-dropped incidents with a real terminal transition ([#13](https://github.com/adamtrip-solutions/fogos-portugal/issues/13)) ([b040112](https://github.com/adamtrip-solutions/fogos-portugal/commit/b040112e182e52315346c78380714395013d86ac))
* **web:** add FogosPortugal brand logo + favicon/PWA icon set ([#8](https://github.com/adamtrip-solutions/fogos-portugal/issues/8)) ([ddaa1ad](https://github.com/adamtrip-solutions/fogos-portugal/commit/ddaa1ad265191679e3d0c19b55c8aba0f332b510))


### Bug Fixes

* **web:** scrollable content pages + reliable map marker clicks ([#12](https://github.com/adamtrip-solutions/fogos-portugal/issues/12)) ([55adf16](https://github.com/adamtrip-solutions/fogos-portugal/commit/55adf168fba936c05e50684a0a513ebc8a39327f))

## [1.0.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v0.1.0...web-v1.0.0) (2026-07-05)


### ⚠ BREAKING CHANGES

* **web:** the web app no longer exposes alert subscriptions or in-app alert toasts; the alertEvents/createAlertSubscription/deleteAlertSubscription server functions are removed.

### Features

* **web:** import fogos-frontend as apps/web ([e0c2ab9](https://github.com/adamtrip-solutions/fogos-portugal/commit/e0c2ab955951f2eb78f655721b47e2f0e7ddad16))
* **web:** remove the Alertas popover, alerts lib, and toast provider ([6b56b32](https://github.com/adamtrip-solutions/fogos-portugal/commit/6b56b32fae057fd7d1fe38535398689553f75940))
