# FogosPortugal

A live map of wildfires in Portugal — active incidents, their status and history, weather, and
fire-risk data, in one fast, open interface. FogosPortugal is a non-profit, ad-free, open-source
project: a clean-room .NET backend and a TanStack Start web app, built to make Portugal's fire
situation legible to anyone, for free.

## Repository structure

This is a pnpm + .NET monorepo:

```
fogos-portugal/
├── backend/            # .NET 10 solution — the API, worker, importer, and admin CLI
│   ├── Fogos.sln
│   ├── src/            # Fogos.Domain, Infrastructure, Api, Worker, Importer, AdminCli
│   ├── tests/          # domain, importer, and integration (Testcontainers) suites
│   ├── dev/seed/       # demo fixtures (incidents, locations, weather stations)
│   ├── renderer/       # Node social-screenshot sidecar
│   └── docker-compose.yml
├── apps/
│   └── web/            # TanStack Start web app (React 19, MapLibre, deck.gl)
├── packages/           # shared TypeScript (api-client, ui-tokens) — added as needed
├── docs/               # product plans, migration & deployment notes, ADRs
├── pnpm-workspace.yaml
└── package.json        # workspace root (private)
```

## Quickstart

### Prerequisites

- [.NET SDK 10.0.100+](https://dotnet.microsoft.com/download)
- Docker + Docker Compose (local infra stack)
- [Node 20+](https://nodejs.org) and [pnpm](https://pnpm.io) (`corepack enable`)

### Backend + demo database

```bash
cd backend

# single-node Mongo replica set (change streams need it), Redis, MinIO
docker compose --profile dev up -d

# seed the demo database (four incidents, three IPMA stations, a few locations)
dotnet run --project src/Fogos.Importer -- seed

# run the API (health at /healthz/live, /healthz/ready)
dotnet run --project src/Fogos.Api

# optional: run the worker
dotnet run --project src/Fogos.Worker
```

Full backend tests (`dotnet test`) spin up throwaway Mongo/Redis/MinIO via Testcontainers.

### Web app

```bash
pnpm install                 # from the repo root — installs the whole workspace
pnpm dev:web                 # http://localhost:3000

# or from apps/web:  pnpm dev
```

The web app reads the API URL from `apps/web/.env.local` (`FOGOS_API_URL`, default
`http://localhost:5077`). The backend has no CORS — the web app calls the API through server
functions, so the browser never talks to it directly.

Root workspace scripts: `pnpm dev:web`, `pnpm build:web`, `pnpm test:web`.

## Credits — built on the shoulders of FogosPT / Fogos.pt

FogosPortugal exists because of **[Fogos.pt](https://fogos.pt)** — the pioneering wildfire-map
project by **[VOST Portugal](https://vost.pt)** (Virtual Operations Support Team). Fogos.pt has,
for years, been *the* reference for tracking wildfires in Portugal, and it defined what a public,
real-time fire map should be. This project stands on that work and is deeply grateful for it.

FogosPortugal is an independent, standalone project — not affiliated with, and not a replacement
for, Fogos.pt. Please support and follow the original: **https://fogos.pt**.

## Data sources

Fire, weather, and aviation data come from public and official feeds, including:

- **ICNF** — Instituto da Conservação da Natureza e das Florestas (official occurrence records)
- **ArcGIS / ANEPC** — civil-protection incident feeds
- **IPMA** — weather observations and forecasts, and WMS fire-risk / radar layers
- **NASA FIRMS** — satellite hotspot detections
- **RainViewer** — precipitation radar tiles
- **Open-Meteo** — wind fields (animated particle layer)
- **Flightradar24 / adsb.fi / airplanes.live** — firefighting-aircraft tracking

All sources are used under their respective terms; credit and thanks to each.

## Non-profit, no ads, open source

FogosPortugal is run as a public service: **no ads, no tracking-for-profit, no paywalls**. It is
open source so anyone can inspect it, learn from it, run it, and contribute. If it is ever useful
to you during a fire, it has done its job.
