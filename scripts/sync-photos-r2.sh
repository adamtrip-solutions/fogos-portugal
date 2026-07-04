#!/usr/bin/env bash
#
# sync-photos-r2.sh — one-shot MinIO → Cloudflare R2 photo migration.
#
# WHY: the legacy platform stores citizen photos in a self-hosted MinIO bucket; the .NET
# rebuild serves them from Cloudflare R2 behind the same public CDN base. Photo documents
# persist only the object KEY (e.g. incidents/<incidentId>/<photoId>.jpg), never a full URL,
# so after the imported photo docs land in the new database every key must resolve on R2.
# This script copies the whole bucket ONCE at switchover, preserving keys byte-for-byte.
# Run it after the final content freeze on the legacy platform and before flipping the
# public CDN base to R2. Re-running is safe (rclone sync is idempotent), but note that
# `sync` DELETES destination objects missing from the source — do not run it after the new
# platform starts writing photos of its own to R2.
#
# PREREQUISITES: rclone ≥ 1.60 with two configured remotes (rclone config):
#
#   [minio]                                  # source — legacy MinIO
#   type = s3
#   provider = Minio
#   access_key_id = <MINIO_ACCESS_KEY>
#   secret_access_key = <MINIO_SECRET_KEY>
#   endpoint = http://<minio-host>:9000
#   force_path_style = true
#
#   [r2]                                     # destination — Cloudflare R2
#   type = s3
#   provider = Cloudflare
#   access_key_id = <R2_ACCESS_KEY_ID>
#   secret_access_key = <R2_SECRET_ACCESS_KEY>
#   endpoint = https://<account-id>.r2.cloudflarestorage.com
#
# USAGE:
#   ./sync-photos-r2.sh            # real run
#   DRY_RUN=1 ./sync-photos-r2.sh  # show what would transfer, move nothing

set -euo pipefail

BUCKET="${BUCKET:-incident-photos}"
SOURCE_REMOTE="${SOURCE_REMOTE:-minio}"
DEST_REMOTE="${DEST_REMOTE:-r2}"

EXTRA_FLAGS=()
if [[ "${DRY_RUN:-0}" == "1" ]]; then
  EXTRA_FLAGS+=(--dry-run)
fi

# --checksum: compare by hash, not modtime/size — MinIO and R2 modtimes differ after upload.
# Keys are preserved verbatim: minio:bucket/<key> → r2:bucket/<key>.
exec rclone sync \
  "${SOURCE_REMOTE}:${BUCKET}" \
  "${DEST_REMOTE}:${BUCKET}" \
  --checksum \
  --transfers 16 \
  --checkers 32 \
  --stats-one-line \
  --progress \
  "${EXTRA_FLAGS[@]+"${EXTRA_FLAGS[@]}"}"
