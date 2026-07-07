# FogosPortugal API (.NET 10)

A standalone, greenfield rebuild of the [FogosPortugal](https://fogosportugal.pt) wildfire API — the
service behind Portugal's live map of active fires, weather, and fire-risk data. It runs on
its own MongoDB with a clean schema and a first-class importer for the decade of historical
data from the old PHP platform, which keeps running untouched until an owner-driven
switchover. All outbound side effects (Twitter, Telegram, Facebook, FCM, Discord posts) stay
in **dry-run** until that switchover.

See [`docs/MIGRATION-PLAN.md`](../docs/MIGRATION-PLAN.md) for the plan and
[`docs/ANALYSIS.md`](../docs/ANALYSIS.md) for the functional reference.

## Layout

- `src/Fogos.Domain` — pure domain: entities, status/natureza catalogs, geo, clock, hashtags.
- `src/Fogos.Infrastructure` — Mongo (class maps, indexes), Redis, S3-compatible storage, Discord ops.
- `src/Fogos.Api` — ASP.NET Core host: GraphQL (HotChocolate 16), REST v3 compat, auth, rate limiting.
- `src/Fogos.Worker` — Quartz host: ingestion, enrichment, and alerting jobs.
- `src/Fogos.Importer` — console tool: dev seed and legacy Mongo import.
- `tests/` — domain unit tests, plus importer/integration placeholders.

## Prerequisites

- [.NET SDK 10.0.100+](https://dotnet.microsoft.com/download)
- Docker + Docker Compose (for the local infra stack)

## Quick start

Bring up the local infrastructure (single-node Mongo replica set, Redis, MinIO):

```bash
docker compose --profile dev up -d
```

Seed dev fixtures (four incidents, three IPMA stations, a few locations):

```bash
dotnet run --project src/Fogos.Importer -- seed
```

Run the API (health endpoints at `/healthz/live` and `/healthz/ready`):

```bash
dotnet run --project src/Fogos.Api
```

Run the worker:

```bash
dotnet run --project src/Fogos.Worker
```

## Tests

```bash
dotnet test
```

## Configuration

Options bind from `appsettings.json` / `appsettings.Development.json` and environment
variables (`Mongo__ConnectionString`, `ObjectStorage__Endpoint`, …). For the importer, the
`FOGOS_`-prefixed form also works (`FOGOS_Mongo__ConnectionString`). Compose-level knobs are
documented in [`.env.example`](.env.example) — copy it to `.env` to override. Every publisher
defaults to `DryRun`; nothing goes live without explicit production configuration.
