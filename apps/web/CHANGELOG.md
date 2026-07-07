# Changelog

## [1.8.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.7.0...web-v1.8.0) (2026-07-07)


### Features

* **web:** deterministic z-priority for overlapping map dots ([#51](https://github.com/adamtrip-solutions/fogos-portugal/issues/51)) ([5b57698](https://github.com/adamtrip-solutions/fogos-portugal/commit/5b576983a885a1a105035f687dbdfe3124407c3d))

## [1.7.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.6.0...web-v1.7.0) (2026-07-06)


### Features

* /conta — Clerk sign-in, API-key self-service, alert management (accounts 3/3) ([#47](https://github.com/adamtrip-solutions/fogos-portugal/issues/47)) ([7bf1dac](https://github.com/adamtrip-solutions/fogos-portugal/commit/7bf1dacda73e51e860425191b116db539dec0694))
* **web:** anchor the resource chart at zero on the alert time ([#44](https://github.com/adamtrip-solutions/fogos-portugal/issues/44)) ([388e648](https://github.com/adamtrip-solutions/fogos-portugal/commit/388e64864de7c008158c6dcfa670315c0ac1d748))
* **web:** hide the Conta nav entry while Clerk is unconfigured ([#49](https://github.com/adamtrip-solutions/fogos-portugal/issues/49)) ([b2d4439](https://github.com/adamtrip-solutions/fogos-portugal/commit/b2d4439b39191fb2f28be96dabdef9b38c115982))


### Bug Fixes

* every incident gets a status timeline from creation ([#46](https://github.com/adamtrip-solutions/fogos-portugal/issues/46)) ([4ae1a35](https://github.com/adamtrip-solutions/fogos-portugal/commit/4ae1a3553a38ef298531bb1e2ecd6497190c9286))

## [1.6.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.5.0...web-v1.6.0) (2026-07-06)


### Features

* **web:** view transitions between pages ([#39](https://github.com/adamtrip-solutions/fogos-portugal/issues/39)) ([ed52773](https://github.com/adamtrip-solutions/fogos-portugal/commit/ed527738c53eefff40b75e7fc9568dee78a76f97))


### Bug Fixes

* **web:** key finished-fire visibility on conclusion time, not updatedAt ([#40](https://github.com/adamtrip-solutions/fogos-portugal/issues/40)) ([c9c2c2c](https://github.com/adamtrip-solutions/fogos-portugal/commit/c9c2c2cc7d8ca79ed41458ef05df40d32bdce1c1))

## [1.5.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.4.0...web-v1.5.0) (2026-07-06)


### Features

* **web:** five status buckets — Vigilância in blue, closed states gray, 3h concluded window ([#37](https://github.com/adamtrip-solutions/fogos-portugal/issues/37)) ([3495740](https://github.com/adamtrip-solutions/fogos-portugal/commit/3495740c6e7fd30260b8ee78ab209c373748b54e))

## [1.4.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.3.0...web-v1.4.0) (2026-07-06)


### Features

* incidents table page + map fire filters ([#34](https://github.com/adamtrip-solutions/fogos-portugal/issues/34)) ([4a9b4c1](https://github.com/adamtrip-solutions/fogos-portugal/commit/4a9b4c1bb65978d27dd2be5455b121d9abb13280))
* map feed keyed on updatedAt (long-running fires stay visible) ([#35](https://github.com/adamtrip-solutions/fogos-portugal/issues/35)) ([ed4aef9](https://github.com/adamtrip-solutions/fogos-portugal/commit/ed4aef934c6812f476676c09ea9d1c82277d2594))

## [1.3.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.2.1...web-v1.3.0) (2026-07-06)


### Features

* **web:** send first-party API key on SSR calls to the API ([#27](https://github.com/adamtrip-solutions/fogos-portugal/issues/27)) ([9926c57](https://github.com/adamtrip-solutions/fogos-portugal/commit/9926c5768147241a94a298a45d72d132cbc35d19))

## [1.2.1](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.2.0...web-v1.2.1) (2026-07-05)


### Bug Fixes

* **web:** light-mode active nav styling and real footer version ([#21](https://github.com/adamtrip-solutions/fogos-portugal/issues/21)) ([6f5dd58](https://github.com/adamtrip-solutions/fogos-portugal/commit/6f5dd5847efda98f9825536676258186ac459ab1))


### Performance Improvements

* **web:** throttle hover hit-test and cheapen halo pulse ([#23](https://github.com/adamtrip-solutions/fogos-portugal/issues/23)) ([84066e4](https://github.com/adamtrip-solutions/fogos-portugal/commit/84066e494041c45c54952a73d9bc142e11a18729))

## [1.2.0](https://github.com/adamtrip-solutions/fogos-portugal/compare/web-v1.1.0...web-v1.2.0) (2026-07-05)


### Features

* **web:** add non-official-source disclaimers ([#18](https://github.com/adamtrip-solutions/fogos-portugal/issues/18)) ([760824e](https://github.com/adamtrip-solutions/fogos-portugal/commit/760824eded4990eab2c8e99ce9e89b94cc786a22))


### Bug Fixes

* **web:** drive hover cursor and ring from the projection hit-test ([#20](https://github.com/adamtrip-solutions/fogos-portugal/issues/20)) ([e748429](https://github.com/adamtrip-solutions/fogos-portugal/commit/e7484290b9435cf54e64bd9c89980743c6252f33))

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
