# fogos.pt API — .NET + HotChocolate Rebuild Plan (v5)

> Companion to `ANALYSIS.md` (the functional reference for everything being rebuilt).
> Authored 2026-07-03. Target stack: **.NET 10 (LTS) / ASP.NET Core / HotChocolate 15 /
> MongoDB.Driver / StackExchange.Redis / Quartz.NET / Cloudflare R2**.
>
> **History:** v1 drafted a byte-compatible strangler-fig port; v2 passed adversarial
> review #1 (12 findings); v3 pivoted to GraphQL-first + PHP façade with auth/rate
> limits per owner direction and passed adversarial review #2 (10 findings); v4 dropped
> the gateway container, scoped compose to the new API only, and moved storage to R2
> behind an abstraction.
>
> **v5 — the standalone pivot (owner decisions on all open questions):** this is now a
> **greenfield project**: new repo, own MongoDB, **clean schema from day one** (the old
> plan's "Phase 7 modernization" becomes the starting point), and a **data importer**
> instead of a shared database. The old PHP platform keeps running untouched and is not
> this project's concern until a final owner-driven switchover. Resolved decisions:
> anonymous reads kept with tight limits · Bluesky deleted · docker compose, no k8s ·
> new repo (git local until a remote exists) · API-key self-service deferred, core API
> first.

---

## 0. Guiding decisions

1. **Standalone greenfield.** New repo, new deployables, **own MongoDB**. The old
   deployment is never touched, patched, or choreographed with — it simply keeps running
   until the owner flips the platform over. `ANALYSIS.md` is the authoritative reference
   for *what* the system does (endpoints' semantics, job cadences, parsers' quirks,
   status tables); nothing obligates us to *how* it stores or shapes anything.

2. **Clean schema from day one + a first-class Importer.** No dual `id`/`_id`, no
   `{sec:…}` remnants, no pre-formatted date strings, no `[lat,lng]`-vs-`[lng,lat]`
   split. Historical data (a decade of incidents, weather, ICNF burn areas) comes in via
   `Fogos.Importer` — an idempotent, re-runnable tool that reads the old Mongo and
   upserts into the new schema, **normalizing the legacy dirt once at import time**
   (whitespace status keys, `1.º` variants, DICO padding, coordinate order) instead of
   carrying compat code forever. It runs repeatedly during development (real data in dev
   from week one) and one last time at switchover as a delta pass.

3. **GraphQL-first, REST v3 for what GraphQL is bad at.** `/graphql` with proper types,
   cursor paging, DataLoaders, and **subscriptions (committed feature)** for the
   fogos.pt frontend and mobile app. REST v3 only for format outputs (KML / GeoJSON /
   CSV for Google Earth & GIS tools), photo multipart upload, `/auth`, and health.

4. **Explicit domain events, not observer magic.** The ingestion pipeline computes a
   ChangeSet per incident and raises `IncidentCreated` / `IncidentResourcesChanged` /
   `IncidentStatusChanged`; handlers (history writers, social, push) subscribe. Visible,
   testable, idempotent.

5. **All outbound side effects are dry-run until switchover.** Even as a standalone
   project, the *external accounts* are shared with the live platform: the Twitter
   account, Telegram channels, FCM topics, and the FR24 credit budget don't know there
   are two stacks. Every publisher has `off / dry-run / on`; everything stays dry-run
   (echoed to a private Discord channel) until the switchover playbook flips channels
   one at a time — the single moment of coordination this plan has left.

6. **Docker compose, three services.** `fogos.api` (Kestrel: GraphQL + v3 + auth),
   `fogos.worker` (Quartz + queue consumers + change-stream subscription bridge),
   `fogos.renderer` (Node/Playwright — required by social screenshots). Dev profile
   adds `mongodb` (single-node replica set — change streams need it), `redis`, and
   `minio` as the local R2 stand-in. Production points at its own managed Mongo/Redis
   and real R2 via config. No gateway container — TLS/routing/edge rate rules are
   Cloudflare's job, Kestrel is the origin.

---

## 1. Solution layout

```
backend/                     # monorepo backend/ (was the fogosapi-dotnet repo root)
  src/
    Fogos.Domain/            # Pure C#: entities, enums, status & natureza mapping,
                             # wave detection (WMO 6-day rule), convex hull, hashtags,
                             # haversine. No I/O — the tricky logic, unit-tested hard.
    Fogos.Infrastructure/    # Mongo repositories, Redis, IObjectStorage (S3-compatible:
                             # R2 prod / MinIO dev), typed HttpClients (ArcGIS, ICNF,
                             # IPMA, FIRMS, FR24, adsb.fi, airplanes.live, GitHub),
                             # social publishers (Twitter, Telegram, Facebook, Discord),
                             # FCM, renderer client, image pipeline.
    Fogos.Api/               # GraphQL + REST v3 + /auth + middleware (auth, rate
                             # limiting, CORS, Cache-Control).
    Fogos.Worker/            # Quartz jobs, Redis Streams consumers (default, icnf),
                             # domain-event handlers, change-stream → subscriptions.
    Fogos.Importer/          # Console tool: old Mongo → new schema. Idempotent upserts,
                             # per-collection mappers, normalization rules, delta mode
                             # (--since), dry-run + report mode.
  tests/
    Fogos.Domain.Tests/      # status tables, natureza classification, waves, hull, tags
    Fogos.Importer.Tests/    # golden tests: legacy fixture docs → expected new docs
    Fogos.Integration.Tests/ # Testcontainers: Mongo + Redis + MinIO; ingest fixtures;
                             # photo pipeline end-to-end
```

### Library choices

| Concern | Choice | Notes |
|---|---|---|
| Mongo | `MongoDB.Driver` | Explicit class maps. Clean schema means **no** polymorphic `_id` serializer, no `[BsonExtraElements]` gymnastics — those died with the shared-DB constraint. |
| GraphQL | `HotChocolate` 15 + `HotChocolate.Data.MongoDb` + `HotChocolate.Subscriptions.Redis` | Code-first. |
| Scheduling | `Quartz.NET` | Trigger table derived from `bootstrap/app.php` cadences (documented next to each Laravel original, including helper semantics and `ShouldBeUnique` → Redis `SET NX` locks). |
| Queue | Redis Streams, consumer groups `default` + `icnf` | Retry ×3, dead-letter collection in Mongo, delayed delivery (FCM's 3-min delay). |
| Object storage | **`IObjectStorage`** (Put/Delete/PublicUrl/Presign), one S3-compatible impl via `AWSSDK.S3` | Config-selected: **Cloudflare R2** in prod (`<account>.r2.cloudflarestorage.com`, region `auto`), MinIO in dev, any S3-compatible later. Public URLs from a configured base (custom domain on the R2 bucket, e.g. `cdn.fogos.pt`); only `storage_key` is persisted. |
| Images | `SixLabors.ImageSharp` + a ported PNG `eXIf` chunk walker | GPS required (422), DMS→decimal, resize ≤2560, progressive JPEG q82, metadata stripped. |
| Push | `FirebaseAdmin` | FCM v1; topic-condition batching (≤5), `dev-` prefix outside production. |
| Scraping | `AngleSharp` + regex | ICNF tables, IPMA pages — with parser-failure Discord alerts and feed-freshness monitors (port the `history.json` staleness idea). |
| CSV / KML | `CsvHelper` (`;`, UTF-8 BOM) / `XmlWriter` | No byte-parity requirement anymore; KML color order is still `AABBGGRR`. |
| Resilience | `Polly` via `Microsoft.Extensions.Http.Resilience` | Per-source retry/backoff; failures → Discord ops. |
| Errors / Time | `Sentry.AspNetCore` / `TimeZoneInfo` `Europe/Lisbon` via a `FogosClock` | No `DateTime.Now` anywhere; stats windows are Lisbon-local. |
| Social | Typed HttpClients (Twitter v2 w/ OAuth1.0a signing, Telegram, Facebook Graph, Discord webhooks) | **Bluesky deleted** (owner decision). VOST/legacy Twitter paths reviewed at port time — only what's actually live. |

### Not ported (from `ANALYSIS.md` §6.9 dead-code list)

`TwitterToolV2`, `CheckNewWarning`, `ExampleJob`, `weather/thunders`, Madeira stubs,
OpenWeather helper, `UpdateICNFData` bucket 7, all commented-out scheduler entries,
legacy ANPC WCF / SharePoint / IMAP ingesters (`ProcessANPCAllDataV2` *is* ported as the
fallback `IIncidentSource`), legacy ADSB Exchange pipeline, `pplanes`-backed endpoint,
troll mode (superseded by §2b auth + 429s).

---

## 1b. Target data model (the old Phase 7, now day one)

Design principles: business keys as `_id` where natural; BSON dates only (rendering is
the API's job); **GeoJSON `[lng,lat]` Points + 2dsphere indexes everywhere**; enums as
canonical codes with mapping done at the edges (ingest/import); TTL where data is
sampling noise after a season.

| Collection | Replaces | Notes |
|---|---|---|
| `incidents` | `data` | `_id` = business id (string, the ANEPC `numero_sado`); `location` GeoJSON Point; `status` {code,label}; `kind` enum (fire/urban/transport/otherFire/fma/other) replacing five `isX` booleans; `resources` sub-doc; `icnf` sub-doc; BSON `occurredAt/createdAt/updatedAt`. |
| `incident_history` | `history` | resource snapshots, FK `incidentId`, BSON dates. |
| `incident_status_history` | `statusHistory` | status transitions, FK `incidentId`. |
| `incident_photos` | `incident_photos` | same concept; `storage_key` (R2), moderation status, GPS as GeoJSON. |
| `social_threads` | fields on `data` | `lastTweetId`, `facebookPostId`, `sentCheckImportant`, `notifyBig` move off the incident into per-incident thread state. |
| `weather_stations` | `weatherStations` | `_id` = IPMA `stationId`; GeoJSON + 2dsphere (the index PHP needed but never migrated). |
| `weather_hourly` / `weather_daily` | `weatherData` / `weatherDataDaily` | upsert key `(stationId, at)`; `-99` sentinels normalized to null **in both** (fixes the daily-path bug). |
| `weather_normals` / `temperature_waves` / `weather_warnings` | same | unchanged concepts; wave upsert key `(stationId, type, startDate)`. |
| `rcm_daily` / `rcm_geojson` | `rcm` / `rcmJS` | risk per concelho per day; GeoJSON payloads per horizon. |
| `warnings` | `warning` + `warning_agif` + `warningSite` | one collection, `kind` discriminator (manual/agif/site) — they differ only in fan-out. |
| `flight_positions` | `flight_positions` | + **TTL index** (retention decision at Phase 3; raw positions are noise after a season — aggregates can be kept). |
| `tracked_aircraft` / `hotspots` / `locations` | same | `locations` gets `dico` precomputed. |
| `api_clients` | — (new) | §2b: hashed keys, tier, scopes. |

**Importer mapping** (per collection, golden-tested): dual `id`/`_id` → single `_id`;
`{sec:…}` & `d-m-Y` strings → BSON dates (Lisbon → UTC); `[lat,lng]` arrays + separate
`lat`/`lng` → one GeoJSON Point; dirty status keys (`'  DESPACHO DE 1º ALERTA'`,
`' Encerrada'`, `1.º` variant) → canonical codes; five `isX` booleans → `kind`; DICO
zero-padding; `-99` → null. Rows that fit no rule land in an `import_quarantine`
collection with the reason — nothing is silently dropped.

---

## 2. GraphQL design

Schema sketch (settled in v3, unchanged in substance):

```graphql
type Query {
  incident(id: ID!): Incident
  incidents(filter: IncidentFilter, after: String, first: Int = 25): IncidentConnection!
  activeIncidents(kind: [IncidentKind!]): [Incident!]!        # defaults to [FIRE]
  stats: Stats!                                               # today/yesterday/week/burnArea/ignitionsHourly…
  weatherStations(place: String): [WeatherStation!]!
  dailyWeather(date: Date!): [DailyWeather!]!
  temperatureWaves(ongoingOnly: Boolean = true): TemperatureWaves!
  fireRisk(day: RiskDay!, concelho: String): FireRisk!        # TODAY / TOMORROW / AFTER
  warnings(kind: WarningKind): [Warning!]!
  aircraft(activeOnly: Boolean = false): [Aircraft!]!
  aircraftTrack(icao: String!, limit: Int = 20): [AircraftPosition!]!
}

type Incident {
  id: ID!
  occurredAt: DateTime!
  location: String! district: String! concelho: String! freguesia: String
  coordinates: GeoPoint
  status: IncidentStatus!           # { code, label, color }
  kind: IncidentKind!
  resources: Resources!             # { man, terrain, aerial, heliFight, planeFight, … }
  active: Boolean! important: Boolean!
  history(first: Int): [ResourceSnapshot!]!      # DataLoader
  statusHistory: [StatusChange!]!                # DataLoader
  photos: [IncidentPhoto!]!                      # DataLoader, approved+public
  weather: WeatherObservation                    # nearest station, DataLoader
  icnf: IcnfData
  hotspots: Hotspots
  fireRisk: ConcelhoRisk                         # via DICO, DataLoader
}

type Mutation {                     # scopes: addPosit/attachKml → write:incidents,
                                    # addWarning(kind) → write:warnings,
                                    # moderatePhoto → moderate:photos
  addPosit(incidentId: ID!, input: PositInput!): Incident!
  attachKml(incidentId: ID!, kml: String!): Incident!
  addWarning(kind: WarningKind!, input: WarningInput!): Warning!   # MANUAL | AGIF
  moderatePhoto(photoId: ID!, decision: ModerationDecision!, publish: Boolean): IncidentPhoto!
}

type Subscription {
  incidentUpdated(id: ID): Incident!
  activeIncidentsChanged: ActiveIncidentsDelta!
  warningAdded: Warning!
}
```

- **Subscriptions**: Mongo change streams (own DB, replica set by construction) → Redis →
  `graphql-ws`. Live map updates for FE/mobile replace the 30s polling of the old hot
  path. Per-tier subscription caps.
- **DataLoaders** for station/RCM/photos/positions — the old N+1s never get born.
- **Guard rails**: depth + cost limits, paging caps, APQ for first-party apps,
  introspection on.
- Photo upload stays REST multipart; hand-written `IncidentFilter` → Mongo filters (no
  generic `[UseFiltering]` on a public API).

## 2b. Auth & rate limiting

Decided: **anonymous reads exist, tightly limited, for casual/small consumers.**
Self-service key portal **deferred** — admin CLI issues keys at launch.

| Tier | Credential | Read | Write | Limits (tunable) |
|---|---|---|---|---|
| `anonymous` | none | ✅ | ❌ except photo upload (own gates) | ~30 req/min/IP, GraphQL cost 500/min, no subscriptions |
| `registered` | API key `fgs_live_…` (`X-API-Key`, hashed in `api_clients`) | ✅ | ❌ | ~300 req/min, cost 5k/min, 2 subscriptions |
| `first-party` | JWT (self-issued, RS256, 15-min + refresh, `POST /auth/token`) | ✅ | scoped | high; full subscriptions |
| `operator` | JWT/key with `write:incidents` / `write:warnings` / `moderate:photos` | ✅ | per scope | standard |

- Public web frontend: designated public site key + `Origin` pinning; limiter partition
  is **`(credential, IP)`** for public-context credentials (each visitor gets their own
  budget; an extracted key buys nothing beyond one IP's worth). Server-held credentials
  partition by credential alone; anonymous by IP. Counters in Redis.
- GraphQL gets **two layers**: request rate + operation-cost budget per client.
- Photo upload keeps its specialized gates (per-IP/min 3, per-incident/IP/hour 8,
  per-incident global/hour 80, pending cap 50) — the deliberate anonymous-write
  exception, since citizen photo submission is the product.
- Mobile apps can upgrade to attested tokens (App Attest / Play Integrity) later without
  server contract changes.

## 3. Ingestion architecture

```
Quartz trigger ──> IIncidentSource (ArcGisOcorrenciasSource | AnepcApiSource fallback)
                        │  fetch + parse → RawIncident list
                        ▼
                IncidentIngestService
                        │  map via canonical tables (status/natureza/location),
                        │  compute ChangeSet vs stored doc, upsert
                        ▼
                DomainEventDispatcher ──> Redis Streams
        ┌───────────────┼──────────────────────┬───────────────────┐
        ▼               ▼                      ▼                   ▼
  HistoryWriter   StatusHistoryWriter   SocialHandlers      NotificationHandlers
                  (reacendimento/       (new/important/     (FCM nearby/status/big;
                   dominado rules)       status posts +      3-min delayed send)
                                         renderer shots)
```

Follow-the-old-cadence trigger table (from `ANALYSIS.md` §3): ArcGIS every 5 min,
HistoryTotal every 2 min, ICNF table every 5 min, planes every 3 min (offset per
source), FIRMS/IPMA-warnings/pending-photos every 15 min, weather hourly + daily,
RCM hourly + 09:00/18:00 social runs, waves 05:00, summaries hourly/09:30, PS-project
08:30. `CheckIsActive` and `CheckImportantFireIncident` (asset threshold 15, age > 3h,
statusCodes 1–6) run after each ingest. FR24 keeps its credit meter (Redis, 95% guard),
daylight window, and active-aerial gate — **with its own dev API key, or disabled, until
switchover** (the credit budget is shared with the live platform).

## 4. Phases

Greenfield build order — no cutover choreography, each phase just makes the product more
complete. Real data arrives in Phase 1 via the Importer, so everything after is
developed against production-shaped data.

**Phase 0 — Foundations (small).** Repo scaffold (git local; remote when owner provides
it), solution + projects, CI (build/test), compose with dev profile, strongly-typed
options, Sentry + Discord ops plumbing, target-schema class maps + seeded fixtures.

**Phase 1 — Importer + read API (medium-large).** `Fogos.Importer` with per-collection
mappers + golden tests + quarantine; run against a dump of the old Mongo. On top: the
full GraphQL read schema, DataLoaders, subscriptions bridge, REST v3 format endpoints
(`active.{kml,geojson,csv}`, perimeter KML incl. the FIRMS convex-hull builder). Exit
criterion: dev instance serves a decade of real data, live-updating as importer re-runs.

**Phase 2 — Auth & rate limiting (medium).** §2b in full: `api_clients` + admin CLI,
`/auth/token`, scope policies, Redis-backed limiter, GraphQL cost analysis, photo-gate
scaffolding.

**Phase 3 — Ingestion pipelines (large).** Order (parser risk ascending, matching §3):
weather → waves → RCM (+PS-project) → FIRMS → planes → IPMA warnings → **incident
cluster** (ArcGIS ingest + ChangeSet events + history/status writers + active/important
checks + ICNF enrichment on the `icnf` group). All social/push handlers run dry-run;
each scraper ships with fixtures, freshness monitoring, and parse-failure alerts.
Ingested docs are diffed against what the importer produces from the old system's output
for the same period — semantic parity with `ANALYSIS.md` as the spec, not byte parity.

**Phase 4 — Photos (medium).** Upload (REST v3 multipart, EXIF pipeline,
`IObjectStorage`→R2), listing, moderation mutation, pending-moderation reminder job.
One-time object sync of existing photos MinIO→R2 (`rclone`, keys preserved) so imported
photo docs resolve. Decide `flight_positions`/photo retention here.

**Phase 5 — Writes & summaries (small-medium).** `addPosit`, `attachKml`,
`addWarning(kind)` mutations with their event handlers; `HourlySummary` /
`DailySummary` jobs. Still dry-run — at this point the new stack is functionally
complete and shadowing reality.

**Phase 6 — Switchover playbook (owner-triggered, the only coordination point).**
1. Freeze: final Importer delta run against the old Mongo.
2. Repoint FE/mobile/consumers at the new API (their own release cycles).
3. Flip: old scheduler off, new publishers `dry-run → on` one channel at a time
   (Discord → Telegram → Facebook → Twitter → FCM), verifying a real incident cycle
   (threading, Facebook comments, push topics). FR24 switches to the production key.
4. The old platform's retirement afterwards is out of this project's scope.
   Until step 3, both stacks may ingest in parallel harmlessly — separate DBs, and only
   one has live side effects.

## 5. Testing strategy

- **Domain unit tests** encode the nasty tables verbatim from the PHP source: full
  status map incl. dirty keys and both `Despacho` variants, all five `NATUREZA_CODE_*`
  arrays, wave rule (incl. month-boundary windows), hull, hashtags (accents preserved),
  haversine, active/important thresholds.
- **Importer golden tests**: real (anonymized where needed) legacy fixture docs →
  expected new-schema docs; quarantine behavior; idempotency (import twice ≡ once).
- **Ingest fixture tests**: recorded ArcGIS/ICNF/IPMA/FIRMS responses → expected
  documents + expected domain events (which then imply the expected social/push
  dry-run output — asserted against the capture channel).
- **Integration tests** (Testcontainers Mongo/Redis/MinIO): repositories, queue retry +
  dead-letter, photo pipeline end-to-end, rate limiter behavior, subscription delivery.
- **Dry-run capture** to a private Discord channel from Phase 3 on — humans eyeball the
  would-have-been posts against what the live platform actually posts.

## 6. Risk register

| Risk | Mitigation |
|---|---|
| Live side effects fire before switchover (shared Twitter/Telegram/FCM/FR24 accounts) | Publishers hard-default to dry-run; `on` requires prod config only used in the switchover playbook; FR24 dev key or disabled. |
| Importer mis-maps a decade of legacy dirt | Golden tests per collection; quarantine (nothing silently dropped); re-runnable; final delta run keeps window small. |
| Scraper breakage (IPMA/ICNF pages change under us) | Fixtures pin expected shapes; freshness monitors + parse-failure Discord alerts distinguish "source changed" from "port is wrong". |
| Status/natureza mapping drift breaks active/important/social logic | Tables ported verbatim + data-driven tests; ingest diffing in Phase 3. |
| Coordinate-order inversion during import/ingest | Single `GeoPoint` type with named factories (`FromLatLng`, `FromGeoJson`); GeoJSON-only storage; per-collection mapper tests. |
| Timezone drift in stats windows | `FogosClock` (Europe/Lisbon) everywhere; edge-time tests for `last-night`/`8hours`-style windows. |
| GraphQL abuse on a public endpoint | Cost analysis + depth caps + per-tier budgets + subscription caps; anonymous squeezed. |
| Renderer coupling (screenshots wait on frontend selectors) | Renderer ported as-is (container + retry/min-bytes client); screenshot failure degrades to text-only post, never blocks the event pipeline. |
| Switchover data gap (docs written to old Mongo after the freeze) | Importer delta mode keyed on `updated` timestamps; freeze window is minutes, not days; playbook includes a post-flip verification pass. |

## 7. Decision log (owner, 2026-07-03)

1. ~~Deprecation window~~ → **standalone project**; old platform not this project's concern.
2. Anonymous reads → **kept, tight limits**, for casual/small consumers.
3. Bluesky → **deleted**.
4. Hosting → **docker compose, no k8s**.
5. Repo → **new repo**; git local until the owner provides the remote URL.
6. Key self-service portal → **deferred**; the API and its functionality are the core.

Remaining small decisions (deferrable, flagged where they land): R2 public domain
(`cdn.fogos.pt` repoint vs new host) — Phase 4; `flight_positions`/photo retention —
Phase 4; FR24 dev credentials vs disabled-in-dev — Phase 3; repo/project naming — Phase 0.

**Resolved during the build (2026-07-04):** naming — solution `Fogos`, projects
`Fogos.*`; `flight_positions` retention — TTL index, 180 days by default
(`Mongo:FlightPositionTtlDays`); photos — retained indefinitely (they are the
product); FR24 in dev — disabled until a key is configured, production key only at
switchover. The switchover playbook lives in `SWITCHOVER.md`.
