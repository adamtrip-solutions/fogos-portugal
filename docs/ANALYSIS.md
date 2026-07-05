# fogos.pt API — Global Analysis (for the .NET + HotChocolate rewrite)

> Produced 2026-07-03 from two independent deep-dives (Claude Opus 4.8 + GPT-5.5/Codex) over
> the Laravel 12 codebase in `fogosapi/`. The two analyses agree on all load-bearing facts.
> Concrete file paths refer to `fogosapi/`.

**Scope:** Laravel 12 / PHP 8.2 wildfire-tracking API. Primary datastore is **MongoDB**
(`mongodb/laravel-mongodb ^5.0`, database `fires`); **Redis** backs both the queue and the
cache; **MinIO** (S3) stores photos; a **Node/Playwright** renderer takes screenshots.
Sentry for errors.

> **Important reconciliation up front.** The repo's own `CLAUDE.md` says the core ingestion
> is `ProcessANPCAllDataV2` every 2 min. That is **stale**. The live scheduler in
> `bootstrap/app.php` (lines 55–98) instead runs **`ProcessOcorrenciasSite` every 5 min**
> (an ArcGIS FeatureServer source) as the active-incident ingester, plus
> `ProcessDataForHistoryTotal` every 2 min. `ProcessANPCAllDataV2` (basic-auth
> `ANEPC_API_URL`) and `ProcessANPCAllData` (old prociv.pt SharePoint endpoint) still exist
> and share the same downstream logic but are **not scheduled**. Port the pipeline as
> source-agnostic: any of the three ingesters produces the same `Incident` shape and
> dispatches the same follow-ups.

---

## 1. Domain & Purpose

fogos.pt aggregates **live wildfire/civil-protection incidents in Portugal** and enriches
them with weather, fire-risk forecasts, satellite hotspots, aerial-asset tracking, and user
photos, then broadcasts to social media and mobile push. Data flows in from **ANEPC**
(civil protection — incidents), **ICNF** (forestry — fire detail, burn area, cause, KML
perimeters), **IPMA** (weather observations, warnings, fire-risk maps, climate normals),
**NASA FIRMS** (satellite thermal hotspots), and flight-tracking providers
(FR24 / ADSB.fi / airplanes.live / ADSB Exchange).

### Data model (all models in `app/Models/`, MongoDB unless noted)

Every model except `User` extends `MongoDB\Laravel\Eloquent\Model`, connection `mongodb`,
string PK `_id` (non-incrementing), and **renames Laravel timestamps to
`created`/`updated`** via `CREATED_AT`/`UPDATED_AT` constants — *except* `IncidentPhoto`,
which keeps `created_at`/`updated_at`. This is a systemic porting gotcha.

| Model | Collection | Key fields / notes |
|---|---|---|
| **Incident** | `data` | Central entity. Constants and scopes in §6. Casts `dateTime/created/updated`→datetime, `lat/lng`→float, `statusCode`→int, most `isX`→bool. Business key is the string `id` (`numero_sado`/`sadoId`); legacy docs also carry an ObjectId `_id`. |
| **IncidentHistory** | `history` | Time series of `man/terrain/aerial/location` per incident; `scopeWhereFireId` matches `incidentId` OR `id`. `Incident::history()` = hasMany `incidentId`↔`id`. |
| **IncidentStatusHistory** | `statusHistory` | Status transitions; `$dateFormat='d-m-Y H:i'`; `{sec:…}` accessors. FK `id`. |
| **IncidentPhoto** | `incident_photos` | `STATUS_PENDING='pending'`, `STATUS_APPROVED='approved'`; fields `fire_id, status, public, signature, storage_key, size_bytes, width, height, mime, gps, taken_at, exif_raw, client, moderation`. Default `created_at/updated_at`. |
| **Location** | `locations` | Geocoding: `level 1 = distrito`, `level 2 = concelho`; `level, code, name`. |
| **RCM** | `rcm` | Fire-risk per concelho: `concelho, date, hoje, amanha, depois, depois2, depois3, dico`. `RCM_TO_HUMAN` 1→Reduzido…5→Maximo; `RCM_TO_EMOJI` 🟢🔵🟡🟠🔴; `getRiskTodayEmoji()`. |
| **RCMForJS** | `rcmJS` | Raw per-day risk payloads: `dataPrev, dataRun, fileDate, local, when` (hoje/amanha/depois). |
| **WeatherStation** | `weatherStations` | `stationId, coordinates([lng,lat]), geoJSON(Point), location, place`. `scopeWhereStationId` matches `stationId` OR `id`. Needs **2dsphere index on `geoJSON`**. |
| **WeatherData** | `weatherData` | Hourly obs upsert `(stationId,date)`; `WIND_DIRECTIONS` map 0→null…9→N. |
| **WeatherDataDaily** | `weatherDataDaily` | Daily aggregates (`temp_max/temp_min/…`); insert-only. |
| **WeatherNormal** | `weatherNormals` | `PERIOD_HEAT='1991-2020'`, `PERIOD_MID='1981-2010'`, `PERIOD_COLD='1971-2000'`; `tmax_mean`/`tmin_mean` are **12-element monthly arrays**. |
| **TemperatureWave** | `temperatureWaves` | `TYPE_HEAT/TYPE_COLD`; upsert `(stationId,type,start_date)`; `ongoing`; `days[]`. |
| **WeatherWarning** | `weather_warnings` | IPMA awareness warnings; `getLevelPT()`; attaches `WeatherWarningObserver`. |
| **Hotspot** | `hotspots` | NASA FIRMS: `incident_id, viirs[], modis[], fetched_at`. |
| **HistoryTotal** | `historyTotal` | Global rolling totals `man/terrain/aerial/total`; `{sec:…}` accessors. |
| **Warning / WarningAgif / WarningSite** | `warning` / `warning_agif` / `warningSite` | Manual broadcast alerts. |
| **WarningMadeira** | `warningMadeira` | Madeira warnings; `scopeWhereWarningId` matches `id` OR `_id`. |
| **FlightPosition** | `flight_positions` | Append-only aircraft samples (`icao, registration, lat, lon, altitude, sampled_at, source, fr24_id`). |
| **TrackedAircraft** | `tracked_aircraft` | Fleet whitelist: `icao, registration, name, type, kind, base, operator, notify, active`. |
| **Planes** | `pplanes` | Legacy raw ADSB Exchange docs. |
| **User** | default RDB (not Mongo) | Stub, effectively unused. |

**Recurring legacy-compat pattern:** `scopeWhereFireId` =
`whereRaw(['$or'=>[['id'=>$id],['_id'=>$id]]])` (`app/Models/Incident.php:211-214`) —
matches both new string-`_id` docs and legacy `{_id:ObjectId, id:"string"}` docs. The Mongo
connection sets `rename_embedded_id_field => false` (`config/database.php:22`) specifically
to keep nested `id` fields intact. The same `$or` pattern appears in IncidentHistory,
IncidentStatusHistory, WarningMadeira, WeatherStation.

---

## 2. HTTP Surface (`routes/web.php`)

Two API families. All CORS-open (`config/cors.php`: `*` origins/methods/headers on
`v1/*, v2/*, fires/*, madeira/*, new/fires`; max-age 86400). No global auth;
write/moderation endpoints check header keys inline or via middleware. Two caching layers:
**server-side** `Cache::remember` (Redis) and **CDN/browser** `Cache-Control` via
`CacheableResponse` (`app/Http/Concerns/CacheableResponse.php`: `s-maxage`=CDN TTL,
`max-age`≈half, `stale-while-revalidate`).

### Legacy / v1 (`LegacyController`, gated by `LEGACY_ENABLE`, no auth)

Reads collection `data` via `Incident` scopes. `/new/fires` (+**troll mode**),
`/fires/data?id=` (time series, cache `legacy.firesData.v1.<md5>` 60s), `/fires/`,
`/fires/danger`, `/fires/status`, `/madeira/fires[/status]` (empty stubs). Under `v1/`:
`warnings`, `warnings/site` (cache 300s), `madeira/warnings`, `now`, `now/data`, `status`
(returns a Portuguese tweet-style **string**), `active` (cache `legacy.active.v1` 30s —
invalidated by ingesters), `aerial` (string), `stats` (string),
`risk`/`risk-today`/`risk-tomorrow`/`risk-after` (cache 900s), `list`. Sub-group
`v1/stats/`: `8hours`, `8hours/yesterday`, `last-night`, `week`, `today`, `yesterday`,
`burn-area` (sums `icnf.burnArea.total`), `motive` (tallies `icnf.tipocausa`+`causafamilia`).
Several endpoints return plain strings, not JSON.

### v2 (current)

- **incidents** (`IncidentController`): `GET search` (paginated; `IncidentSearchRequest`
  validates `day/before/after` as `Y-m-d`, `all/extend` boolean; filters
  `concelho/fma/naturezaCode/subRegion`; `csv2` export; default fire-only unless `all`);
  `GET active` (default fire-only via `buildActiveQuery` lines 277–310 — category flags
  `fire/fma/otherfire` are OR'd, `all=1` removes the filter; supports `geojson`
  (FeatureCollection), `csv`, `csv2`; cache 30s; **troll mode** injects a fake incident for
  unauthorized UA/referer, lines 150–221, response then `no-store`); `GET active/kml`
  (XMLWriter KML, `s-maxage=30`, `echo`+`die`); `GET 1000ha-burned`
  (`icnf.burnArea.total > 1000`); `GET {id}/kml` / `{id}/kmlVost` (stored KML streamed as
  attachment); `GET {id}/kmlFirms` (**generated** KML — convex-hull AOI over incident +
  VIIRS/MODIS hotspots, styles in `buildFIRMSKml` lines 644–763, KML colors `AABBGGRR`);
  `POST {id}/posit` and `POST {id}/kml` (**`API_WRITE_KEY`** header, lines 522–524/543–545;
  the KML post triggers a Renderer screenshot + tweet, lines 555–584). Photos:
  `GET {id}/photos` (approved+public, `s-maxage=300`), `POST {id}/photos/all` (inline
  `PHOTO_MODERATION_KEY`), `POST {id}/photos` (public upload, middleware `photo.ratelimit`).
- **moderation/photos** (`PhotoModerationController`, middleware `photo.modauth`):
  `GET /` (queue), `POST {photoId}/approve` (optional social fan-out + push),
  `POST {photoId}/reject` (deletes MinIO object + doc).
- **weather** (`WeatherController`): `thunders` (**stub** `{message:'soon'}`), `stations`,
  `daily?date=` (410 without date), `waves` (Redis `weather:waves` 3600s; heat=1991-2020 /
  cold=1971-2000 sections from ongoing `TemperatureWave`s), `ipma-services` (proxies IPMA
  WMS GetCapabilities XML, rewrites http→https, `echo`+`die`).
- **rcm** (`RCMController`): `today`/`tomorrow`/`after` (GeoJSON concelho polygons + risk
  via `RCMTool`), `parish?concelho=`, `update` (**unauthenticated** synchronous
  `ProcessRCM` trigger — flag for the port).
- **planes** (`PlanesController`): `index` (tracked + latest position; N+1 query), `active`
  (positions within 10 min), `recent?hours=` (cache 60s), `{icao}/track` (last 20
  `FlightPosition`), `{icao}` (last 20 legacy `Planes`).
- **warnings** (`WarningsController`): `POST add`, `POST add/agif` (**`API_WRITE_KEY`**;
  persist + broadcast to all social channels incl. Bluesky).
- **stats** (`StatsController`): `today/ignitions-hourly?day=` (hourly ignition histogram).
- **other** (`OtherController`): `mobile-contributors` (GitHub API proxy, Redis 30h).

**Response shapes** — `app/Resources/`: `IncidentResource` (embeds
`dateTime/created/updated` as `{sec:unixTs}` objects; injects live `weather` from nearest
station + haversine `stationDistance`; optional `history`/`statusHistory` with `extend`);
`IncidentPhotoResource` / `IncidentPhotoModerationResource`; `PlaneResource` /
`PlaneRecentResource`; `V1/HistoryTotalResource`, `V1/HistoryStatusResource`,
`V1/WarningResource`. **KML/XML:** 4 KML producers + WMS XML proxy. **CSV:** `fputcsv`
with UTF-8 BOM, `;` delimiter, in `active` and `search`.

Cache TTLs: firesData 60s · active 30s · warningsSite 300s · risk-* 900s · planes.recent
60s · weather:waves 3600s · weather:{lat}:{lng} 10800s · mobile:contributors 108000s.

---

## 3. Background Work

**Scheduler** (`bootstrap/app.php:55-98`, only when `SCHEDULER_ENABLE`; cron runs
`artisan schedule:run` per minute — `.docker/scheduler/cron.d/artisan`). Jobs extend
`App\Jobs\Job` (ShouldQueue, Redis). Deployment runs two workers: `default` queue and a
dedicated **`icnf`** queue (`docker-compose.yml`).

| Cadence | Job | Source → writes → side effects |
|---|---|---|
| every 2 min | `ProcessDataForHistoryTotal` | Sums man/aerial/terrain over active fires (statusCode ∈ 3,4,5,6) → appends `HistoryTotal` only when changed. |
| every 5 min | **`ProcessOcorrenciasSite`** | ArcGIS `services-eu1.arcgis.com/.../OcorrenciasSite/FeatureServer/0/query` (paginated JSON, 1000/page) → upsert `Incident` → `Cache::forget('legacy.active.v1')`, dispatch `CheckIsActive` + `CheckImportantFireIncident`; freshness tracking via `history.json` hash → Discord alerts on stale/recovered feed. |
| every 5 min | `ProcessICNFNewFireData` | ICNF `faztable.asp` (JS-array in HTML) + per-id XML `webserviceocorrencias.asp` → create/update `Incident` (hardcodes naturezaCode `3103` "Mato"; counts `-1` sentinel = ICNF-only). |
| every 3 min (offsets 0/1/2) | `ProcessFR24Planes` / `ProcessAirplanesLivePlanes` / `ProcessAdsbfiPlanes` | Flight APIs → append `FlightPosition`; FR24 posts first-sighting to Twitter/FB/push (30-min gap dedup, `ProcessFR24Planes.php:113-150`). |
| every 15 min | `HandleWeatherWarnings` | Scrapes `ipma.pt/pt/index.html` JS var `result_warnings` → `WeatherWarning` (md5 `control` dedup); observer sends STB-district warnings to a Telegram channel. |
| every 15 min | `ProcessFIRMSData` | NASA FIRMS CSV (VIIRS+MODIS, ±0.10° bbox per active fire) → `Hotspot`. |
| every 15 min | `CheckPendingPhotoModeration` | Pending count → Discord (cooldown). |
| hourly@0 | `HourlySummary` | Active totals → screenshot → Twitter/Bluesky/FB/Telegram. |
| hourly | `ProcessRCM(false)` | IPMA fire-risk ingest (no social). |
| 4h/12h/daily/cron | `UpdateICNFData(0..6)` | Age-bucketed re-scrape → dispatches `ProcessICNFFireData` per incident onto the `icnf` queue. |
| 09:00 / 18:00 | `ProcessRCM(true)` / `ProcessRCM(true,true)` | Ingest + publish today/tomorrow risk maps to social. |
| 03:21 / hourly | `UpdateWeatherStations` / `UpdateWeatherData` | IPMA stations + hourly observations (`-99`→null sanitization, `UpdateWeatherData.php:85-92`). |
| 04:21 | `UpdateWeatherDataDaily` | IPMA daily observations (insert-only; **no `-99` sanitization**). |
| 05:00 | `DetectTemperatureWaves` | WMO 6-day rule → `TemperatureWave` (+Discord on creation). |
| 08:30 | `SendRiskPSProject` | Latest RCM for one DICO → Telegram. |
| 09:30 | `DailySummary` | Yesterday's stats → Twitter (+VOST retweet)/FB/Telegram/Bluesky. |
| disabled (commented) | `ProcessMadeiraWarnings`, `ProcessPlanes`, `CleanICNFFires`, `HandleANEPCImportantData`, `HandleANEPCPositEmail`, `SendRiskPRProject` | Present but not scheduled. |

**Core ingestion pipeline end to end:**

1. **Ingest** (`ProcessOcorrenciasSite` / `ProcessANPCAllDataV2`): per record
   `Incident::whereFireId(numero)` → `prepareData()` maps raw fields to the canonical
   document (`ProcessOcorrenciasSite.php:142-245`, `ProcessANPCAllDataV2.php:148-235`):
   Location lookup (concelho level-2 → DICO zero-padded to 4 chars → distrito level-1,
   `getLocationData`; Spain special-cased with DICO `00`); status via
   `Incident::STATUS_ID`/`STATUS_COLORS` (+ alias maps
   `STATUS_LOOKUP_ALIASES`/`STATUS_DISPLAY_FIXES`, `ProcessOcorrenciasSite.php:20-27`);
   natureza classified through the five `NATUREZA_CODE_*` arrays into
   `isFire/isUrbanFire/isTransporteFire/isOtherFire/isOtherIncident/isFMA`. New docs get
   `sentCheckImportant=false`, `important=false`, zeroed heli/plane counters,
   `anepcDirectUpdate=false`. ArcGIS variant adds `estadoAgrupado, faseIncendio, rasi,
   duracaoMinutos, endereco, dataDosDados`, operacionais breakdowns.
2. **Persistence side effects** (`app/Observers/IncidentObserver.php` — a boot **trait** on
   the model): created → `AssignNearestWeatherStation` (Mongo `$near` on
   `weatherStations.geoJSON`); if `dateTime.year>=2022` → `SaveIncidentHistory`,
   `SaveIncidentStatusHistory`; if `isFire` → `HandleNewIncidentSocialMedia` +
   `ProcessICNFFireData`; always → `HandleNewIncidentEmergenciasSocialMedia`,
   `NotificationTool::sendNearbyNotification` + `sendNewIncidentNotification`; naturezaCode
   `2409` → Discord aero alert; DICO-whitelisted HL/PR/PS project Telegram jobs (lines
   52–80). Updated → re-assign station if lat/lng dirty; re-run both history jobs.
3. **`CheckIsActive`**: `active=true` incidents whose `id` is absent from the current feed
   → `active=false`.

   > **.NET divergence (feed-drop close-out).** The clean pipeline deliberately drops the
   > legacy flag-only `CheckIsActive` semantics. Flipping `active=false` on the *first* miss,
   > leaving status/history untouched, froze incidents mid-life: the ANEPC OcorrenciasSite
   > layer lists *current* occurrences, so a mid-fire disappearance is a feed gap, not a
   > resolution — yet those records became invisible (`activeIncidents` filters `Active==true`)
   > and aged out of every map view with no terminal transition ever recorded.
   > `ProcessOcorrenciasSiteJob.CloseOutMissingAsync` replaces it: every seen id stamps
   > `LastSeenInFeedAt`; an active incident absent past the `CloseAfterMissingFor` grace
   > (30 min) gets a real terminal transition to status **13 — "Encerrada (sem atualização)"**
   > (history row + `IncidentStatusChanged`, exactly like a feed-driven change). The sweep is
   > guarded: it runs only when the feed is fresh this sweep (a frozen feed can't signal an
   > ending) and aborts with an ops alert if candidates exceed `max(3, MaxCloseFraction × active)`
   > (0.25) to survive a truncated feed. A closed-out id reappearing revives through the normal
   > upsert (13 → active code), and 13→"Em Curso" counts as a status-regression rekindle.
   > Two option names keep the two concerns honest: `FeedStaleAfter` gates the whole-feed
   > freshness alert; `CloseAfterMissingFor` is the per-incident grace.
4. **`CheckImportantFireIncident`** (ShouldBeUnique): active fires, statusCode 1–6,
   `sentCheckImportant=false`, `aerial+terrain > IMPORTANT_INCIDENT_TOTAL_ASSETS` (15),
   older than 3h → push + Twitter/Telegram/Bluesky/Facebook "incêndio importante" post;
   sets `sentCheckImportant=true`, stores `lastTweetId` for threading.
5. **Status-change social** (`SaveIncidentStatusHistory.php:69-119`): fires only —
   "🚨 Reacendimento" when `Em Curso` ← {Conclusão, Em Resolução, Vigilância};
   "✅ Dominado" when {Conclusão, Em Resolução} ← `Em Curso`; each with Renderer screenshot
   of `fogo/{id}/detalhe` waiting on `.leaflet-tile-loaded`, threaded tweet via
   `lastTweetId`, FB/Telegram/Discord; Facebook comment on the stored `facebookPostId`
   documenting the transition; always `sendNewStatusNotification` push; then appends the
   `statusHistory` row. Job re-fetches the incident (`updateIncident`, line 135) before
   acting.
6. **Data-change social** (`SaveIncidentHistory`): posts on new POSIT/COS;
   `man >= BIG_INCIDENT_MAN` (100) → "big incident" tweet + VOST retweet + push.
7. **ICNF enrichment** (`ProcessICNFFireData`, `icnf` queue): XML detail → `icnf`
   sub-document (burnArea povoamento/agrícola/mato/total, cause taxonomy, alert source),
   downloads KML perimeter from `fogos.icnf.pt/sgif2010/ficheiroskml/{id}.kml`; first-seen
   cause/KML/burn-area triggers social posts with screenshots.

---

## 4. External Integrations

**Consumed:**

- **ANEPC:** ArcGIS FeatureServer (above); `ANEPC_API_URL` with Basic auth
  (`ANEPC_API_USERNAME/PASSWORD`), optional `PROXY_ENABLE`/`PROXY_URL`; legacy
  `www.prociv.pt/_vti_bin/ARM.ANPC.UI/ANPC_SituacaoOperacional.svc/GetHistoryOccurrencesByLocation`
  (POST JSON); SharePoint `www.prociv.pt/bk/_api/Web/Lists(guid'…')/Items(39)/FieldValuesAsHtml`
  (HTML table by column index → cos/pco/important, `HandleANEPCImportantData.php:62-87`);
  Gmail IMAP `{imap.gmail.com:993/ssl}` (`VOST_EMAIL`/`VOST_EMAIL_PASSWORD`, sender filter
  `MAIL_ANEPC_FROM`) parsing POSIT emails for heliFight/planeFight/heliCoord/cos/pco
  (`HandleANEPCPositEmail.php:64-95`).
- **ICNF:** `fogos.icnf.pt/localizador/faztable.asp`, `webserviceocorrencias.asp?ncco={id}`
  (XML), KML files, plus PDFs from `ICNF_PDF_URL` parsed with
  `spatie/pdf-to-text`/`smalot/pdfparser`. TLS verification disabled.
- **IPMA:** `api.ipma.pt/open-data/observation/meteorology/stations/{stations,observations}.json`;
  daily observations; homepage JS scrape for warnings;
  `www.ipma.pt/pt/riscoincendio/rcm.pt/index.jsp` for fire risk (regex over `rcmF[]`);
  WMS `mf2.ipma.pt`; climate normals via `weather:import-normals` command.
- **NASA FIRMS:**
  `firms.modaps.eosdis.nasa.gov/api/area/csv/{NASA_FIRMS_KEY}/{VIIRS_SNPP_NRT|MODIS_NRT}/{bbox}/1`.
- **Flight tracking:** FR24 (`fr24api.flightradar24.com`, Bearer `FR24_API_KEY`,
  live-positions-light by registration; Redis monthly credit meter
  `fr24:credits:month:YYYY-MM`, 95% budget guard, daylight-window gate sunrise+1h→sunset−1h
  Lisbon, only when active incidents have aerial>0 — `ProcessFR24Planes.php:38-62,152-162`);
  ADSB.fi (`ADSBFI_URL`); airplanes.live (`AIRPLANES_LIVE_URL`); legacy ADSB Exchange
  (`ADSBEXCHANGE_API_KEY`, fleet Google Sheet `PLANES_LIST_SPREADSHEET`).
- **Misc:** ProCiv Madeira notifications API; GitHub contributors API; OpenWeather
  (`OPENWEATHER_API`, partially dead code).

**Outbound** (`app/Tools/`, env-driven, feature-flagged):

- **Twitter/X** (`TwitterTool`, noweh v2 + legacy TwitterAPIExchange for VOST/Emergencias
  accounts): threaded replies via `lastTweetId`, media upload, 280-char thread splitting,
  `retweetVost`. `TwitterToolV2.php` is empty.
- **Telegram** (`TelegramTool`): `api.telegram.org/bot{token}` sendMessage/sendPhoto; main
  `@fogospt` channel + HL/PR/PS project channels (with `message_thread_id`).
- **Discord** (`DiscordTool`): raw cURL webhooks, three channels (general / aero / errors);
  `postError` bypasses the `DISCORD_ENABLE` flag.
- **Facebook** (`FacebookTool`): Graph API feed/photos/comments on `FACEBOOK_PAGE_ID`;
  Emergencias-page publisher is a commented-out no-op.
- **Bluesky** (`BlueskyTool`): `bsky.social/xrpc` createSession/createRecord — currently
  disabled by an early `return`.
- **Firebase FCM** (`NotificationTool` → `SendFcmNotification` with 3-min delay): FCM HTTP
  v1, service-account OAuth from hardcoded `/var/www/html/credentials.json`; topic
  conditions capped at 5; `dev-` topic prefix outside production; legacy
  `web-/mobile-android-/mobile-ios-` topics when `LEGACY_ENABLE`.
- **Node renderer** (`Renderer`/`RendererCapture` → `renderer/server.js`):
  `POST {RENDERER_URL}/render` with `{url,width,height,waitFor,minBytes}`; renders
  `https://{SCREENSHOT_DOMAIN}/{path}`; retries 1s/3s/9s, min-size guard; temp PNG under
  `storage/app/tmp/screenshots/`. Service is Fastify + Playwright Chromium, context pool of
  3, contexts recycled every 50 uses, `networkidle` + selector wait + 1.5s settle,
  pre-seeded CookieConsent cookie for `.fogos.pt`.
- **MinIO/S3** (`PhotoStorageTool`, disk `minio`): path-style, bucket `incident-photos`,
  public URL base `MINIO_PUBLIC_BASE_URL` (`cdn.fogos.pt/incident-photos`).

---

## 5. Cross-cutting

- **Auth:** No user auth anywhere. Write endpoints compare `header('key')` to
  `API_WRITE_KEY` inline (`IncidentController.php:522-524`, `WarningsController`).
  Moderation uses `PhotoModerationAuth` (timing-safe `hash_equals` on
  `PHOTO_MODERATION_KEY`); `photos/all` duplicates the check inline. **Troll mode**
  (`TROLL_MODE`): unauthorized UA (`UA1/UA2/UA3`) or Referer (hardcoded
  fogos/emergencias/ArcGIS whitelist) gets a fake "Utilização indevida" incident injected
  (id `123123123123`), with the caller's UA/referer echoed in `extra`; response `no-store`.
  Present in `IncidentController::active` and `LegacyController::newFires`.
- **Rate limiting** (`PhotoUploadRateLimit`): 3 RateLimiter gates — per-IP/min (3),
  per-IP-per-incident/hour (8), per-incident global/hour (80) — plus pending cap
  50/incident; 429 + `Retry-After`.
- **CORS/CSRF:** CORS `*` on API prefixes, `HandleCors` appended globally; CSRF validation
  disabled for `*`; trust all proxies (`bootstrap/app.php:100-103`).
- **Redis:** cache store `redis` DB 1 (prefix `fogospt_cache`), queue DB 0; both
  `Cache::remember` and raw `Redis::set(...,'EX',ttl)` patterns; FR24 credit counters.
- **Geospatial:** `[lng,lat]` ordering in stored coordinates/geoJSON (but
  `Incident.coordinates` = `[lat,lng]`!); nearest station via `$near` (2dsphere);
  `ConvexHullTool` = Jarvis march + ~500 m buffer in naive degree-space; haversine (R=6371)
  in `IncidentResource` and moderation.
- **Image/EXIF** (`ImageProcessingTool`): PNG-only uploads; custom PNG `eXIf` chunk walker
  → PEL EXIF parse; **GPS required** (422 otherwise); DMS→decimal, altitude/heading;
  `DATE_TIME_ORIGINAL` parsed `Y:m:d H:i:s` in app tz → UTC; output re-encoded to
  progressive JPEG q82, long edge ≤2560px, metadata stripped.
- **Feature flags:** `SCHEDULER_ENABLE, LEGACY_ENABLE, TELEGRAM_ENABLE, TWITTER_ENABLE,
  FACEBOOK_ENABLE, DISCORD_ENABLE, NOTIFICATIONS_ENABLE, FR24_ENABLE, ADSBFI_ENABLE,
  AIRPLANES_LIVE_ENABLE, TROLL_MODE, PROXY_ENABLE`, FR24 credit limit,
  `BIG_INCIDENT_MAN=100`, `IMPORTANT_INCIDENT_TOTAL_ASSETS=15`. All read via `env()` at
  runtime (config-caching hostile; PHP loose truthiness).
- **Deployment:** nginx + php-fpm + mongo + redis + minio + renderer + scheduler (cron) +
  2 queue workers (`default`, `icnf`). Timezone `Europe/Lisbon`. Index plan in
  `database/migrations/2026_05_25_120000_create_mongodb_indexes.php`.

---

## 6. Complexity Hotspots & Migration Risks

1. **Status mapping & dirty data** (`app/Models/Incident.php:174-205`): `STATUS_ID` maps 7
   PT labels→codes (4,3,7,8,9,5,6). `STATUS_COLORS` has **mixed string and int keys**,
   including two dirty keys with leading spaces (`'  DESPACHO DE 1º ALERTA'`,
   `' Encerrada'`); label `'Despacho'`→`FF6E02` but code `3`→`CE773C` (inconsistent by
   design). `ACTIVE_STATUS_CODES=[3,4,5,6]`, `NOT_ACTIVE=[7..12]` but
   `CheckImportantFireIncident` uses `[1..6]`. Status-string normalization: source sends
   `Despacho de 1.º Alerta` but the app stores/displays `Despacho de 1º Alerta` (fix at
   `ProcessANPCAllDataV2.php:230-232`); ArcGIS aliases at `ProcessOcorrenciasSite.php:20-27`.
2. **Legacy `id` vs `_id`:** dual-shape documents require the `$or` scope everywhere an
   incident is fetched by business id; `rename_embedded_id_field=false` is load-bearing.
   `CheckIsActive` queries `whereNotIn('id', …)` only — legacy docs without a top-level
   `id` are unaffected by deactivation.
3. **Scrapers/parsers (most brittle):** IPMA warnings JS-var scrape with `%uXXXX` decoding
   and magic offsets (`HandleWeatherWarnings.php:54-67`); IPMA RCM regex over a JSP page +
   a giant inline concelho GeoJSON literal (`ProcessRCM.php:40-49+`); ICNF `faztable.asp`
   regex parsing; ICNF PDF line-index parsing (contains live bugs: `alertFom` typo,
   `unlink()` on a resource); SharePoint/IMAP HTML tables parsed by **column position**
   (cols 11/12/13/15/16).
4. **KML generation:** four producers; KML color order `AABBGGRR`; hull needs ≥3 vertices;
   `activeKML` and `ipma-services` bypass the framework with `header()`+`echo`+`die` —
   likewise the CSV exports (`exit()` mid-controller).
5. **Wave detection** (`DetectTemperatureWaves.php:70-163`): WMO rule — 6 consecutive days
   where `temp_max − monthly tmax_mean > +5` (heat, vs 1991-2020 normal) or
   `temp_min − tmin_mean < −5` (cold, vs 1971-2000); 10-day lookback; per-day month normal
   so windows spanning month boundaries mix normals; keeps the **latest** qualifying
   window; `ongoing` iff window end = today or yesterday; resets all prior `ongoing` flags
   each run; Discord only on `wasRecentlyCreated`. Risk: daily job doesn't sanitize IPMA
   `-99` sentinels.
6. **Hashtag generation** (`HashTagTool`): `'#IR' . preg_replace('/\s+|\-/','',$concelho)`
   — accents and case preserved; Emergencias variant uses plain `'#'` prefix.
7. **Social threading state on the incident document:** `lastTweetId`, `facebookPostId`,
   `sentCheckImportant`, `notifyBig` live on the `data` doc and are mutated by multiple
   jobs — port as first-class state, and note jobs re-fetch (`firstOrFail`) before writing
   to dodge lost updates.
8. **Date/timezone quirks:** everything in `Europe/Lisbon` (`'Europe/lisbon'` lowercase
   typo works in PHP — verify .NET tz ids); ArcGIS millisecond epochs; `date`/`hour`
   stored as **pre-formatted strings** (`d-m-Y`, `H:i`) alongside `dateTime`;
   `IncidentStatusHistory.$dateFormat='d-m-Y H:i'`; `{sec:…}` timestamp objects in
   responses.
9. **Dead/disabled code — do not port blindly:** `TwitterToolV2` (empty), `BlueskyTool`
   (early return), `FacebookTool::publishEmergencias` (no-op), `CheckNewWarning` (empty
   stub), `ExampleJob`, `weather/thunders` stub, Madeira fire stubs, all commented-out
   scheduler entries. `UpdateICNFData` defines 8 buckets (0–7) but only 0–6 are scheduled.
10. **Security items to fix in the rewrite:** unauthenticated `GET /v2/rcm/update`
    dispatching work synchronously; env-key auth compared with `!==` (not constant-time)
    on write endpoints; `verify=false` TLS on ICNF/IPMA calls; Discord `postError`
    ignoring the enable flag.

**Files that define the contract:** `bootstrap/app.php`, `routes/web.php`,
`app/Models/Incident.php`, `app/Observers/IncidentObserver.php`,
`app/Jobs/ProcessOcorrenciasSite.php` + `ProcessANPCAllDataV2.php`, `CheckIsActive.php`,
`CheckImportantFireIncident.php`, `SaveIncidentStatusHistory.php`,
`SaveIncidentHistory.php`, `ProcessICNFFireData.php`, `UpdateICNFData.php`,
`DetectTemperatureWaves.php`, `ProcessRCM.php`, `app/Resources/IncidentResource.php`,
`app/Tools/*`, `config/database.php`,
`database/migrations/2026_05_25_120000_create_mongodb_indexes.php`.
