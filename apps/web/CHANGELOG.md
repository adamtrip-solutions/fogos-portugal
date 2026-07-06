# Changelog

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
