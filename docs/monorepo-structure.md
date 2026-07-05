# FogosPortugal — monorepo structure

Companion to `deployment-plan.md` (§2) and `mobile-app-plan.md`. This pins the target layout with
the mobile app and shared packages in the picture.

## 1. Layout

```
fogosportugal/                       # renamed from fogosapi-dotnet — keeps backend git history
├── backend/                         # the .NET solution, moved intact (git mv src/tests/Fogos.sln)
│   ├── Fogos.sln
│   ├── src/  (Domain, Infrastructure, Api, Worker, Importer, AdminCli)
│   └── tests/
├── apps/
│   ├── web/                         # current fogos-frontend (no commit history to lose)
│   └── mobile/                      # Expo app (scaffolded when mobile work starts)
├── packages/
│   ├── api-client/                  # @fogos/api-client — GraphQL documents + TS types + fetch core
│   └── ui-tokens/                   # @fogos/ui-tokens — status colors/buckets, PT labels, formatters
├── deploy/                          # compose.yml, cloudflared, versions.env, runbook (server stack only)
├── docs/                            # this file, plans, ADRs, runbooks
├── .github/workflows/               # ci.yml, release-please.yml, deploy.yml, mobile-release.yml
├── pnpm-workspace.yaml              # packages: [apps/*, packages/*]
├── release-please-config.json
└── CLAUDE.md, .editorconfig, README.md
```

Principles:
- **`backend/` stays top-level**, not under `apps/` — different toolchain, its own test tree, and
  path-filtered CI; forcing it into a pnpm-workspace-shaped folder buys nothing.
- **`apps/` + `packages/`** is the standard pnpm workspace split: apps consume packages via
  `workspace:*`; packages are private (never published to npm), so they need no independent
  release train — their changes ship inside whichever app releases.
- **Share logic, not UI.** `api-client` and `ui-tokens` are dependency-light TypeScript. React/DOM
  components stay in `apps/web`; RN components in `apps/mobile`. The temptation to share components
  across DOM/RN is how monorepos rot.
- The legacy Laravel `fogosapi` does NOT come along — it stays where it is until decommissioned.

## 2. What moves into the shared packages (extraction map)

From today's `apps/web/src/lib/fogos/`:

| Today (web) | Moves to | Consumed by |
|---|---|---|
| `api.ts` query strings (`INCIDENT_DETAIL_QUERY`, stats/concelho/alert docs) | `api-client/documents.ts` | web server-fns, mobile direct |
| `types.ts` (hand-written schema mirrors) | `api-client/types.ts` | both |
| bare `graphql<T>()` fetch helper | `api-client/fetch.ts` (takes a base-URL/fetch injection — web injects server-side env, mobile injects the public URL) | both |
| `format.ts` (statusBucket, STATUS_BUCKET_COLOR, formatDuration, criticalReason labels), `hotspots.ts`, `stats.ts`, `kml.ts`, `concelhos.ts` | `ui-tokens` (naming: tokens + pure domain helpers) | both |
| server-fn wrappers, queryOptions factories, React components | stay in `apps/web` | web only |

The existing vitest suites for those helpers move with them (packages get their own `vitest run`).

## 3. Versioning & releases (Release Please manifest mode)

```jsonc
// release-please-config.json
{
  "packages": {
    "backend":     { "component": "api",    "release-type": "simple" },
    "apps/web":    { "component": "web",    "release-type": "node" },
    "apps/mobile": { "component": "mobile", "release-type": "node",
                     "extra-files": ["app.json"] }   // bumps expo version for runtimeVersion policy
  }
}
```

- Tags/changelogs per component: `api-vX.Y.Z`, `web-vX.Y.Z`, `mobile-vX.Y.Z`, each with its own
  `CHANGELOG.md` and release PR (as the user chose: releases tailored to each part).
- `packages/*` are intentionally NOT components: a change there is only observable through an app,
  so conventional commits touching `packages/**` should use the affected app scope
  (`feat(web): …`, `feat(mobile): …`, or both PRs when both are affected). Convention documented
  in CONTRIBUTING; enforced socially, not mechanically.
- Version detection is path-based per component (Release Please reads the paths each commit
  touches), which is exactly the "version detection per part" requirement.

## 4. CI path filtering (Blacksmith)

| Job | Triggers on paths | Runs |
|---|---|---|
| backend-test | `backend/**` | dotnet build + full test suite (Testcontainers) |
| web-test | `apps/web/**`, `packages/**` | tsc, vitest, vite build |
| mobile-test | `apps/mobile/**`, `packages/**` | tsc, eslint, jest-expo |
| packages-test | `packages/**` | package-local vitest (fast feedback before app jobs) |
| api/web images | release `api-v*` / `web-v*` | GHCR arm64 build+push → deploy job on the Mac |
| mobile release | release `mobile-v*` | `eas update` (JS-only) or `eas build`+`submit` (native change) |

Note `packages/**` fans out to BOTH app test jobs — shared code changes must prove both consumers.

## 5. Migration steps (1 sitting, low risk)

1. Commit current work in `fogosapi-dotnet`; rename the GitHub repo → `fogosportugal`.
2. `git mv` the .NET bits into `backend/` (history follows moves; one commit).
3. Copy `fogos-frontend` in as `apps/web` (it has no commits); add `pnpm-workspace.yaml`; fix the
   web app's lockfile location (workspace root `pnpm-lock.yaml`).
4. Extract `packages/api-client` + `packages/ui-tokens` from `apps/web/src/lib/fogos` (mechanical;
   web imports switch to `@fogos/*`; run the moved tests).
5. Land `.github/workflows` (ci.yml first), `release-please-config.json`, `deploy/`.
6. `apps/mobile` arrives later via `create-expo-app` — the structure above already has its seat.

Steps 1–3 before any new feature work lands; step 4 can ride along with the first mobile commit if
preferred (extraction is most valuable the moment a second consumer exists).

## 6. Decision asks

1. Confirm `fogosportugal` as repo name and the rename (redirects from the old name are automatic
   on GitHub).
2. OK that packages have no independent versions (they ship inside app releases)?
3. Extraction timing: do §2 now (clean but touches lots of web imports) or defer until the mobile
   scaffold exists (recommended — extraction with two real consumers avoids speculative API design)?
