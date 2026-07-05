# FogosPortugal — deployment plan

Target: production on a home-server MacBook running OrbStack, public at `fogosportugal.pt`
(**domain/DNS still pending** — everything that depends on the public hostname is blocked until it
lands), CI on GitHub Actions with Blacksmith runners, automated versioning/releases/changelog.

> **Status (2026-07):** the monorepo now lives at `adamtrip-solutions/fogos-portugal` (private) with
> `backend/` + `apps/web/`. Domain, Cloudflare tunnel, and CD are still to come.

## 0. TL;DR decisions

| Concern | Decision |
|---|---|
| Forge / repo shape | GitHub, **one monorepo** (`adamtrip-solutions/fogos-portugal`) holding backend + web + deploy stack |
| CI runners | **Blacksmith** for everything untrusted (PR tests, builds) |
| CD to the Mac | **Self-hosted GitHub runner on the Mac**, deploy jobs only, pull-based `docker compose up` |
| Images | GHCR (`ghcr.io/<you>/fogosportugal-{api,worker,frontend}`), **arm64** |
| Ingress | **Cloudflare Tunnel** (no port-forwarding, TLS + DDoS shielding for free) |
| Versioning | **Release Please (manifest mode)** + Conventional Commits → per-component versions, changelogs, and releases (`api-vX.Y.Z`, `web-vX.Y.Z`) |
| Runtime | One `docker compose` stack in OrbStack: mongo (1-node replica set), redis, api, worker, frontend; photo storage is **external Cloudflare R2** (managed, off-stack) |

## 1. Runtime topology (what runs where)

Everything runs as linux/arm64 containers inside OrbStack on the Mac, one compose stack:

```
cloudflared ──► frontend (TanStack Start SSR, node)  ──► api (internal http)
            └─► api (Fogos.Api)                      ──► mongo, redis, R2 (external)
                worker (Fogos.Worker)                ──► mongo, redis + outbound polling
                mongo  — single-node replica set (REQUIRED: change streams power GraphQL subscriptions)
                redis  — streams (event bus) + subscriptions + rate limits
                R2     — Cloudflare R2 photo storage (external managed, S3-compatible; not a stack service)
```

Routing via the tunnel: `fogosportugal.pt` → frontend `:3000`; `api.fogosportugal.pt` → api `:8080`
(frontend server functions call the API over the compose network, `FOGOS_API_URL=http://api:8080` —
CORS stays irrelevant in prod exactly like in dev).

Notes:
- Mongo must be started with `--replSet rs0` + a one-shot init container that runs `rs.initiate()`.
  Without it the ChangeStreamBridge (subscriptions) silently can't run.
- Images must be **arm64** (Apple Silicon). Build them on Blacksmith's arm64 runners — native, no QEMU.
- Volumes: `mongo-data` on the OrbStack VM disk (photo objects live in R2, not on a local volume).

## 2. Repo shape & branching

**Done:** merged into one monorepo `adamtrip-solutions/fogos-portugal`:

```
/backend    ← the .NET solution (kept its git history — this repo became the monorepo)
/apps/web   ← the web app (was fogos-frontend; had one commit, folded in without history)
/apps/mobile ← Expo app (later)
/packages   ← shared TS (api-client, ui-tokens) — added as needed
/deploy     ← compose file, .env.example, cloudflared config, runbook (still to be split out)
/docs       ← plans, ADRs, runbooks (product-level, stays at root)
/.github    ← workflows
```

(The backend's `docker-compose.yml` currently lives in `backend/`; it moves to `/deploy` when the
production stack is assembled.)

Why monorepo: the GraphQL schema couples the two hard (this week proved it — every backend WP had a
frontend consumer); one deploy target; one release train; atomic schema+UI changes in one PR. The
legacy `fogosapi` (Laravel) stays where it is, untouched, until decommissioned.

Branching: trunk-based. `main` is protected (PRs only, CI green required). No develop branch — the
release mechanics below make `main` always releasable.

## 3. CI — GitHub Actions on Blacksmith

Blacksmith is a drop-in runner provider: you install their GitHub App and change `runs-on:`. Nothing
else about Actions changes. **All PR/untrusted code runs on Blacksmith, never on the home runner.**

`ci.yml` (on: pull_request, push to main), path-filtered jobs:

- **backend-test** — `runs-on: blacksmith-8vcpu-ubuntu-2404-arm`
  `dotnet build` + `dotnet test`. The integration suite uses Testcontainers (mongo/redis/minio);
  Blacksmith runners have Docker, so the full 400+ suite runs as-is. Cache NuGet
  (`useblacksmith/cache` is their faster drop-in for actions/cache).
- **frontend-test** — `runs-on: blacksmith-4vcpu-ubuntu-2404-arm`
  `pnpm install --frozen-lockfile`, `tsc --noEmit`, `vitest run`, `vite build`.
- **images** (main only) — arm64 `docker build` + push to GHCR, tagged `sha-<short>` + `edge`.
  Use `useblacksmith/build-push-action` (their accelerated buildkit with persistent layer cache).

Dockerfiles needed (part of implementing this plan): `backend/Dockerfile.api`,
`backend/Dockerfile.worker`, `apps/web/Dockerfile` (node SSR: `vite build` → `node .output/server`).

## 4. CD — how deploys reach the Mac

The Mac sits behind home NAT, so pushes can't reach it; the Mac must pull. The cleanest GHA-native
way: a **self-hosted runner on the Mac** that only ever runs the deploy job.

How runners work, in one paragraph: a runner is a small agent process you install on a machine; it
long-polls GitHub over outbound HTTPS (no inbound ports needed) and executes workflow jobs that
declare `runs-on: [self-hosted, home]`. You register it once with a token from repo settings. So:
Blacksmith runners (their cloud) run tests/builds; your Mac's runner runs only the 20-line deploy
job that does `docker compose pull && docker compose up -d`.

Safety rails for the home runner (important):
- Register it to the single repo, labels `[self-hosted, home]`.
- Deploy workflow triggers **only** on release publish / tag push / manual `workflow_dispatch` —
  never on `pull_request`. PRs from forks must never schedule jobs on it (repo setting: require
  approval for outside collaborators; also keep the deploy job in a GitHub **environment** named
  `production` so it's gated and audited).
- The runner user only needs docker access; it never builds code, only pulls signed-in GHCR images.

`deploy.yml`: on release published →
1. `docker compose -f deploy/compose.yml pull` (images `:vX.Y.Z` from the release)
2. `docker compose up -d --wait` (compose healthchecks: `/healthz/ready` for api, a `/` probe for frontend)
3. smoke check: GraphQL `{ stats { activeFires } }` + `/v3/feeds/incidents.rss` via the tunnel URL
4. on failure: `docker compose up -d` with the previous pinned tag (kept in `deploy/.current-version`) → rollback is one re-run.

Alternative considered and rejected: Watchtower/cron-pull (no deploy logs/gating/rollback in GitHub,
harder to reason about); cloudflared-exposed webhook (reinvents the runner with more attack surface).

## 5. Versioning, releases, changelog — Release Please

Adopt **Conventional Commits** (`feat:`, `fix:`, `feat!:`/`BREAKING CHANGE:`) — enforce with a PR-title
check (squash-merge so the PR title becomes the commit).

**Release Please in manifest mode** (per-component releases — one release train per part of the
monorepo). `release-please-config.json` declares the components by path:

```jsonc
{
  "packages": {
    "backend":  { "component": "api" },      // → tags api-vX.Y.Z,      backend/CHANGELOG.md
    "apps/web": { "component": "web" }       // → tags web-vX.Y.Z, apps/web/CHANGELOG.md
  }
}
```

- **Version detection is path-based**: a conventional commit touching `backend/**` bumps only the
  api component (fix→patch, feat→minor, breaking→major); `apps/web/**` bumps only web. A PR
  touching both bumps both. Each component gets its own rolling release PR, its own `CHANGELOG.md`,
  its own semver, and its own tag/GitHub Release.
- Merging a component's release PR cuts *that component's* release: tag `api-vX.Y.Z` →
  build/push `fogosportugal-api` + `-worker` images `:X.Y.Z` → deploy job restarts only those
  services. `web-vX.Y.Z` does the same for the web (frontend) container. Compose pins each service
  to its own component version (`deploy/versions.env`).
- Nothing deploys without an explicit release-PR merge; you can ship a frontend fix without
  touching the running API, and vice versa.
- Schema-coupled changes (backend field + frontend consumer in one PR) simply produce two release
  PRs; merge backend's first, frontend's after — the deploy order matches the compatibility order.

## 6. Ingress, DNS, TLS — Cloudflare Tunnel

> ⛔ **Blocked on domain.** This whole section is gated on securing/pointing the `fogosportugal.pt`
> domain (DNS still pending). The tunnel container, ingress rules, and public routing can't be set
> up until the domain is in hand. Until then the stack runs locally only.

- Move `fogosportugal.pt` DNS to Cloudflare (free plan is fine).
- `cloudflared` runs as a container in the stack with two ingress rules
  (`fogosportugal.pt` → `http://frontend:3000`, `api.fogosportugal.pt` → `http://api:8080`).
- You get: TLS, HTTP/3, caching/DDoS shielding, and **zero open ports on your home network**.
  RSS feeds and the tile-heavy map benefit from Cloudflare edge caching (respecting the
  Cache-Control headers the API already sets).
- Set Cloudflare cache rules: bypass for `/graphql`, cache `/v3/feeds/*` and `/api/weather-tiles`.

## 7. Mac-as-server operational hardening

Sleep is already handled: the server is a dedicated Mac configured to never sleep (distinct from
the dev laptop that caused the historical data gaps). Remaining items:

- **Auto-start**: OrbStack: enable "Start at login" + login item; compose services all get
  `restart: unless-stopped`. macOS auto-login for the server user; `System Settings → Energy →
  Start up automatically after a power failure`.
- **Runner as a service**: `./svc.sh install && ./svc.sh start` (the runner ships a launchd wrapper).
- **Backups**: nightly `mongodump --archive --gzip` (cron/launchd) to a second disk + offsite (e.g.
  `rclone` to B2/S3; the archive of a few hundred MB is trivial). Photo objects are in Cloudflare
  R2 (managed) — use R2 bucket versioning / a lifecycle policy there. Test a restore once before
  calling this done.
- **Monitoring**: the stack already has `/healthz/*` + a Discord ops notifier. Add one external
  watcher that isn't on the Mac — healthchecks.io (free) pinged by a launchd cron on the Mac
  ("I'm alive") + an HTTP check on `fogosportugal.pt`. If the Mac dies, you hear about it.
- **Updates**: macOS auto-updates OFF for the server user (they reboot at will); update manually.

## 8. Secrets

- GitHub → environment `production` secrets: GHCR is automatic (`GITHUB_TOKEN`), Cloudflare tunnel
  token, anything the smoke test needs.
- On the Mac: one `deploy/.env` (never committed) with Mongo/Redis passwords, Cloudflare R2 storage
  credentials, and the JWT RSA PEM. Compose injects per-service. Backup this file with the
  same offsite mechanism (it's the real disaster-recovery artifact).

## 9. Rollout order (suggested implementation sequence)

1. Commit the current uncommitted work in both repos (it's all reviewed + green).
2. ✅ Restructure into the monorepo (`backend/`, `apps/web/`), push to `adamtrip-solutions/fogos-portugal`.
3. Dockerfiles + `deploy/compose.yml`; prove the full stack runs locally in OrbStack against demo data.
4. `ci.yml` on Blacksmith (tests only) — get PRs gating.
5. Image publish job → GHCR.
6. ⛔ **Blocked on domain** — Cloudflare: DNS + tunnel container; bring the site up manually once
   (`compose up` by hand). Needs `fogosportugal.pt` (DNS pending).
7. Self-hosted runner on the Mac + `deploy.yml` + environment gating; first automated deploy.
8. Release Please (manifest mode) + conventional-commit PR check; first per-component release
   end-to-end (`api-v*` and `web-v*`).
9. Hardening pass: backups + restore test, healthchecks.io, runbook in `deploy/README.md`.

Each step is independently verifiable; the site is live from step 6, automation completes 7–8.

## 10. Open questions (need your input)

1. ✅ **Resolved.** Repo is `adamtrip-solutions/fogos-portugal` (private).
2. ⛔ **Pending.** Cloudflare account for the domain — OK to move DNS there? (The tunnel approach,
   and the whole public rollout, depends on it. The `fogosportugal.pt` domain/DNS is still pending.)
3. ✅ **Resolved / done.** Folded `fogos-frontend` in as `apps/web` (no history to lose); the
   backend repo became the monorepo.
4. ✅ **Resolved.** FogosPortugal is a **standalone, independent project** — not a drop-in
   replacement for the legacy platform and not fronting its traffic. There are **no redirect
   concerns**: nothing needs to be routed away from `fogos.pt`/`fogosapi`. The relationship is
   **credit only** (see the README credits section — FogosPT / Fogos.pt is the pioneering project
   this builds on and is prominently credited).
5. Blacksmith: you mentioned it for "external runners" — the plan uses it for ALL CI. Any reason to
   keep some CI on GitHub-hosted runners instead?
