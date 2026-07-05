# FogosPortugal — deployment runbook

The production stack for `adamtrip-solutions/fogos-portugal`: one `docker compose` stack in
OrbStack on the home-server Mac (arm64), fronted by a **host-level Cloudflare tunnel** the
operator already runs (shared with other services — there is **no** `cloudflared` container
here). Only `web` and `api` publish ports, bound to `127.0.0.1`; the tunnel maps public
hostnames onto them.

```
Cloudflare tunnel (host)  ──►  127.0.0.1:${WEB_PORT}  →  web  (TanStack Start SSR)
                          └─►  127.0.0.1:${API_PORT}  →  api  (Fogos.Api)
internal network only:    mongo (rs0) · redis · worker
external managed:         Cloudflare R2 (photo storage, off-stack)
```

Images are pulled from GHCR, pinned by `deploy/versions.env`. CI/build/release lives in
`.github/workflows/`; this stack only ever *pulls + runs*.

---

## 1. First boot (on the server Mac)

Prereqs: OrbStack installed with **Start at login** enabled; the machine set to never sleep
and to auto-start after power loss.

```sh
# 1. Clone the monorepo into the persistent state dir the deploy workflow expects.
git clone https://github.com/adamtrip-solutions/fogos-portugal.git ~/fogos-deploy
cd ~/fogos-deploy

# 2. Create the two untracked state files that live NEXT TO the volumes, not in git:
cp deploy/.env.example ~/fogos-deploy/.env         # fill in secrets (see below)
cp deploy/versions.env ~/fogos-deploy/versions.env  # pinned image tags (deploy edits this)
```

> **Why `~/fogos-deploy/{.env,versions.env}` and not `deploy/`?** The deploy workflow keeps
> code (the checked-out `deploy/compose.yml`) separate from state (secrets + pinned versions).
> It reads both files from this dir via `--env-file`, so `git pull` never clobbers your secrets
> or fights the version pins. Override the location with the `DEPLOY_STATE_DIR` repo variable.

Edit `~/fogos-deploy/.env`: fill in the Cloudflare R2 `STORAGE_*` values (see §8), set a stable
`AUTH_RSA_PRIVATE_KEY_PEM` (generate with `dotnet run --project backend/src/Fogos.AdminCli -- keys`
helpers or your own RSA key — leaving it empty auto-generates an ephemeral key that dies on
restart), and `SENTRY_DSN`. Leave `FR24_MODE` at `DryRun` until you intend to spend FR24 credits.

Log in to GHCR once so `pull` works (a classic PAT with `read:packages`, or `gh auth token`):

```sh
echo "$GHCR_TOKEN" | docker login ghcr.io -u <your-github-user> --password-stdin
```

Bring it up (before any release exists, `versions.env` points at `latest`):

```sh
cd ~/fogos-deploy
docker compose --env-file .env --env-file versions.env -f deploy/compose.yml up -d --wait
```

Verify locally:

```sh
curl -fsS "http://127.0.0.1:$(grep ^API_PORT= .env | cut -d= -f2)/healthz/ready"
curl -fsS "http://127.0.0.1:$(grep ^WEB_PORT= .env | cut -d= -f2)/" -o /dev/null -w '%{http_code}\n'
```

---

## 2. Cloudflare tunnel — the two mappings to add by hand

Domain is still pending; add these once it lands. In **your existing host tunnel** config
(`~/.cloudflared/config.yml` or the Zero Trust dashboard), add two ingress rules pointing at
the host-local ports (defaults shown; match your `.env`):

| Public hostname          | Service                  |
|--------------------------|--------------------------|
| `fogosportugal.pt`       | `http://127.0.0.1:3000`  |
| `api.fogosportugal.pt`   | `http://127.0.0.1:8080`  |

`cloudflared` runs at the host level (systemd/launchd or `cloudflared tunnel run`), shared with
your other services — nothing about it lives in this compose stack. After adding the rules,
`cloudflared` reload picks them up; no restart of the stack needed.

Suggested Cloudflare cache rules once live: bypass `/graphql`, cache `/v3/feeds/*` and
`/api/weather-tiles` (the API already sets `Cache-Control`).

---

## 3. Self-hosted runner (enables automated deploys)

The deploy workflow runs `runs-on: [self-hosted, home]`. Install the runner **scoped to this
one repo** (never org-wide):

```sh
# GitHub → repo Settings → Actions → Runners → New self-hosted runner (macOS/arm64).
# It gives you a fresh token; then:
mkdir -p ~/actions-runner && cd ~/actions-runner
# (download + extract the runner tarball per the page's copy-paste block)
./config.sh --url https://github.com/adamtrip-solutions/fogos-portugal \
            --token <RUNNER_TOKEN> \
            --labels self-hosted,home \
            --name fogos-home --unattended

# Install as a login service so it survives reboots:
./svc.sh install
./svc.sh start
```

The runner user only needs Docker access — it never builds code, only pulls GHCR images and
runs compose. Until it exists, the `deploy` workflow just stays queued; that is expected and
harmless.

Safety rails (already enforced by the workflows, but be aware):
- `deploy.yml` triggers **only** on release publish + manual dispatch — never on `pull_request`.
- It is gated behind the `production` GitHub Environment (create it — see §7).
- `if: github.event.repository.fork == false` blocks fork-triggered runs.

---

## 4. Demo vs production database

- **Production** uses `MONGO_DATABASE=fogos` (the default in `.env`). The pipeline (worker)
  fills it from live sources.
- **Demo** data (deterministic, live-looking sample) is produced by the AdminCli seeder and
  targets `fogos_demo` (it *refuses* to touch `fogos`). To explore the product without a live
  pipeline, point the stack at the demo DB and seed it:

  ```sh
  # temporarily expose mongo, seed fogos_demo, then set MONGO_DATABASE=fogos_demo in .env
  FOGOS_Mongo__ConnectionString="mongodb://localhost:27017/?directConnection=true" \
    dotnet run --project backend/src/Fogos.AdminCli -- demo-seed --drop
  ```

Keep production on `fogos`. The demo DB is for staging/QA only.

---

## 5. Deploys, versioning, rollback

Releases are cut by **Release Please** (manifest mode) — merging a component's release PR tags
`api-vX.Y.Z` or `web-vX.Y.Z`, which builds+pushes images (`images.yml`) and then runs
`deploy.yml`. The deploy job pins the new tag in `versions.env` and does `compose pull` +
`up -d --wait` + smoke checks.

- `API_VERSION` → `api`, `worker` images (the backend release train).
- `WEB_VERSION` → `web` image.

**Manual deploy / redeploy:** Actions → *Deploy* → *Run workflow* → pick `component` + `version`.

**Rollback (fast):** edit `~/fogos-deploy/versions.env`, set the component back to a known-good
version, then either re-run the *Deploy* workflow with that version, or on the box:

```sh
cd ~/fogos-deploy
docker compose --env-file .env --env-file versions.env -f deploy/compose.yml pull
docker compose --env-file .env --env-file versions.env -f deploy/compose.yml up -d --wait
```

Every image tag is immutable in GHCR, so rollback is deterministic.

---

## 6. Backups

The real disaster-recovery artifacts are Mongo and `~/fogos-deploy/.env`. Photo objects live in
Cloudflare R2 (managed) — enable bucket versioning / a lifecycle policy there rather than backing
up a local volume; the stack holds no photo state.

Nightly `mongodump` (launchd/cron example — a few hundred MB gzipped):

```sh
# ~/fogos-deploy/backup.sh
set -euo pipefail
STAMP=$(date +%F)
OUT=~/backups/fogos
mkdir -p "$OUT"
docker compose --env-file ~/fogos-deploy/.env -f ~/fogos-deploy/deploy/compose.yml \
  exec -T mongo mongodump --db "${MONGO_DATABASE:-fogos}" --archive --gzip \
  > "$OUT/mongo-$STAMP.archive.gz"
# Offsite (e.g. rclone to B2/S3) + keep a copy of ~/fogos-deploy/.env there too.
find "$OUT" -name 'mongo-*.archive.gz' -mtime +14 -delete
```

Schedule daily (crontab): `15 4 * * * /bin/sh ~/fogos-deploy/backup.sh`. **Test a restore once**
(`mongorestore --archive --gzip --drop`) before trusting it.

---

## 7. One-time GitHub settings you must set by hand

These can't be created from the workflow files and are **required**:

1. **`production` environment** — repo Settings → Environments → *New environment* →
   `production`. Optionally add required reviewers and any environment secrets the deploy needs.
   `deploy.yml` references `environment: production`.
2. **Allow Actions to create PRs** — repo Settings → Actions → General → *Workflow permissions*
   → enable **"Allow GitHub Actions to create and approve pull requests."** Release Please
   opens the rolling release PRs with `GITHUB_TOKEN`; without this it fails.
3. *(optional)* **`DEPLOY_STATE_DIR` repo variable** — only if you cloned somewhere other than
   `~/fogos-deploy`.
4. *(recommended)* Branch protection on `main`: require the `backend` and `web` CI checks; and
   *Require approval for all outside collaborators* under Actions → General (so fork PRs can't
   schedule the home runner).

CI/build runners (Blacksmith) are already available via the org's Blacksmith GitHub App — no
per-repo setup beyond the runner labels the workflows already use.

---

## 8. Object storage — Cloudflare R2

Photo upload/serve uses **Cloudflare R2** (S3-compatible, managed, off-stack). The app is
R2-native (`ForcePathStyle=true`, `Region=auto`); the stack boots fine with the `STORAGE_*` keys
empty — the photo features just stay inert until you set them. Setup:

1. **Create the bucket** — Cloudflare dashboard → R2 → *Create bucket* (e.g. `incident-photos`).
2. **Create an API token** — R2 → *Manage R2 API Tokens* → *Create* with **Object Read & Write**
   permission, scoped to that bucket. Copy the **Access Key ID** and **Secret Access Key**.
3. **Enable public access** for read-serving — either the bucket's **r2.dev** subdomain (quick) or
   a **custom domain** (recommended, e.g. `media.fogosportugal.pt`). This URL is what browsers hit.
4. **Fill in `.env`:**
   - `STORAGE_ENDPOINT` = `https://<accountid>.r2.cloudflarestorage.com` (the S3 API endpoint)
   - `STORAGE_ACCESS_KEY` / `STORAGE_SECRET_KEY` = the token pair from step 2
   - `STORAGE_BUCKET` = the bucket name
   - `STORAGE_PUBLIC_BASE_URL` = the public URL from step 3

R2's free tier covers 10 GB of storage with **zero egress fees**, which suits the photo workload.
The **dev** stack (`backend/docker-compose.yml`) keeps a local **MinIO** stand-in instead, so a
laptop run needs no cloud credentials.
