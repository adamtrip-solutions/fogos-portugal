# Switchover Playbook

The single moment this project coordinates with the old platform. Everything before
this document is safe to run indefinitely alongside production: both stacks may
ingest in parallel (separate databases), and only the old one has live side effects —
every publisher here is hard-defaulted to `DryRun`, and FR24 runs on a dev key or
stays disabled.

Owner-triggered. No step is time-pressured except the data freeze (minutes).

---

## 0. Preconditions (verify before starting)

- [ ] New stack deployed and stable: `docker compose up` green, `/healthz/ready` 200,
      Worker logs show all Quartz jobs firing on cadence for **at least one week**.
- [ ] Dry-run capture channel (Discord) has been reviewed against the live platform's
      actual posts for that week: same incidents detected, same important/big
      escalations, status transitions (reacendimento/dominado) match, RCM/summary
      copy reads right. Deviations understood and accepted.
- [ ] A full `Fogos.Importer` run has completed against a recent dump/replica of the
      old Mongo with **zero unexplained quarantine rows**
      (`import_quarantine` — every row either fixed or consciously waived).
- [ ] Photos synced once already: `scripts/sync-photos-r2.sh` (initial bulk copy can
      run days early; the final delta is fast).
- [ ] Production secrets staged (not enabled): Twitter/Facebook/Telegram credentials,
      FCM service account + production project, FR24 **production** key, R2 keys,
      `cdn.fogos.pt` (or chosen host) pointing at the R2 bucket.
- [ ] API keys issued via `Fogos.AdminCli` for: the web frontend (public-context,
      Origin-pinned), the mobile apps (first-party), each operator (scoped), and any
      known external consumers (registered).
- [ ] Rollback rehearsed: repointing consumers back to the old API is a DNS/config
      change; the old platform is untouched throughout and keeps working.

## 1. Freeze & final delta import (minutes)

1. Announce a short maintenance window to operators (no operator writes on the old
   platform during the freeze).
2. Run the final delta: `Fogos.Importer import --source <old-mongo> --since <last-run>`.
3. Run `scripts/sync-photos-r2.sh` one last time (delta objects only).
4. Spot-check: incident counts per year match between stacks; the currently active
   incident list is identical; latest photos resolve on R2 URLs.

## 2. Repoint consumers (their own release cycles — can span days)

- Web frontend → new `/graphql` (+ subscriptions endpoint) with its site key.
- Mobile apps → new API with first-party token flow (app-store release cadence).
- Known REST consumers → `/v3` equivalents (KML/GeoJSON/CSV).
- Keep the old API serving during this window — nothing conflicts.

## 3. Flip side effects — one channel at a time

Order chosen so mistakes are cheap first: Discord → Telegram → Facebook → Twitter → FCM.

For each channel:
1. Set `Publishing:Channels:<channel>` from `DryRun` to `On` on the **new** stack.
2. Disable the corresponding publisher on the **old** stack (its env flag) —
   never run both `On` for the same channel.
3. Verify one real incident cycle end-to-end on that channel before moving to the
   next: new-incident post, threading on the follow-up (status change replies chain
   onto the first tweet; Facebook comments land on the original post), screenshot
   renders, correct hashtags/copy.
4. FCM last, and only after the mobile repoint has enough adoption: switch
   `Fcm:ProjectId`/credentials to production (topics lose the `dev-` prefix
   automatically in the Production environment) and disable old-platform pushes in
   the same change.

Then:
- FR24: swap the dev key for the production key; confirm the credit meter reads the
  shared monthly budget and the 95% guard is intact.
- Only now turn off the old platform's scheduler entirely (it has no remaining live
  channels; leaving it ingesting harms nothing but wastes API quota).

## 4. Post-flip verification (first 48h)

- [ ] Dry-run capture channel is now quiet (nothing left in DryRun that should be On).
- [ ] Feed-freshness monitors green; no parser-drift alerts.
- [ ] Social threading verified on a multi-update incident.
- [ ] Push received on a real device for a new incident near a subscribed concelho.
- [ ] Rate-limit 429s look sane (no legitimate consumer starved).
- [ ] `import_quarantine` and `dead_letters` both empty of new rows.

## 5. Rollback (any point before step 3 completes)

Repoint consumers back to the old API; set any flipped channels back to `DryRun` and
re-enable them on the old platform. The old stack never stopped being able to serve.
After step 3 is fully complete and stable, rollback means re-flipping channels back —
data written to the new Mongo in the interim does not exist in the old one, so
rolling back *after* days of live operation requires a reverse export (not planned;
decide deliberately).

## Out of scope

Retiring the old platform's infrastructure afterwards is the owner's call and not
part of this project.
