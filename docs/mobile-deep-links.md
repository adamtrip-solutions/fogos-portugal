# Mobile deep links — hosting & credential checklist

Phase 1.4 wires up the mobile app's deep links. This doc covers the two hosted
association files and the values that must be filled before app/universal links
verify in production. The app-side config (scheme, `associatedDomains`,
Android `intentFilters`) already lives in `apps/mobile/app.config.ts`.

## Link shapes handled

| Link | Opens | How |
| --- | --- | --- |
| `fogosportugal://incident/{id}` | map, fire selected | `app/incident/[id].tsx` redirects to `/?incident={id}` |
| `https://fogosportugal.pt/?incident={id}` | map, fire selected | index reads the `incident` search param |
| `https://fogosportugal.pt/` | map | index, no selection |

The `incident` param name is identical to what the web app pushes
(`apps/web/src/routes/index.tsx`) — do not rename it in one place only.

## Hosted files (served from the web app's `public/`)

Both live in `apps/web/public/.well-known/`:

- `apple-app-site-association` — Apple AASA (JSON, **no file extension**).
- `assetlinks.json` — Android Digital Asset Links.

The web app is TanStack Start (Vite). `vite build` copies `apps/web/public/` into
`dist/client/`, and `apps/web/server.mjs` serves `dist/client` via
`srvx/static` **before** the SSR handler (same path `robots.txt` already takes).
So after deploy they resolve at:

- `https://fogosportugal.pt/.well-known/apple-app-site-association`
- `https://fogosportugal.pt/.well-known/assetlinks.json`

Both must be served over HTTPS with no redirect.

### Verify before shipping

1. **Dotfile copy.** Confirm `dist/client/.well-known/` actually contains both
   files after `pnpm --filter web build` (Vite copies dotfiles by default, but
   verify — some copy steps skip `.`-prefixed dirs):
   `ls apps/web/dist/client/.well-known/`.
2. **Association content type.** `apps/web/server.mjs` handles both association
   paths explicitly and serves them as `application/json; charset=utf-8`.

## Signing identifiers

### Apple Team ID

The Apple Developer Team ID is `2A56G82R2N`. The full app ID is
`2A56G82R2N.pt.fogosportugal.app`.

Where to find it: <https://developer.apple.com/account> → Membership details →
"Team ID". (Also visible in an app's Identifiers page as the App ID Prefix.)

### Android signing certificate

The association file contains the SHA-256 fingerprint of the EAS production
keystore created for `pt.fogosportugal.app`. Because Google Play re-signs with
Play App Signing, add the Play app-signing fingerprint after the app is created
in Play Console:

```
eas credentials            # → Android → production → shows the SHA-256
```

or from the Play Console: Release → Setup → App integrity → App signing key
certificate → SHA-256 certificate fingerprint. Add every signing key that must
verify (upload key and Play app-signing key) as separate array entries if links
should verify on internal-testing builds too.

## Bundle id / package

`pt.fogosportugal.app` for **both** iOS `bundleIdentifier` and Android `package`
(already set in `app.config.ts`; the generated `android/` project uses the same
`pt.fogosportugal.app` namespace). The association files hard-code it — keep them
in sync if it ever changes.

## What remains manual

- Add the Google Play app-signing fingerprint to `assetlinks.json` after Play
  App Signing is enabled. Keep the EAS fingerprint for direct/internal builds.
- Deploy the web app so both association files are live before submitting builds.
- Native config only takes effect after a fresh `eas build` (associated domains
  and intent filters are baked into the binary — OTA updates can't add them).
- Verify Universal Links on a device: long-press the `?incident=` URL → "Open in
  FogosPortugal". Android App Links verify automatically once `assetlinks.json`
  is reachable and the fingerprint matches (`adb shell pm get-app-links
  pt.fogosportugal.app` to inspect verification state).
