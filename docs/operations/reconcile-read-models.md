# Operational Guide: Reconciling Read Models

## Overview

Read models in RoleRadar maintain denormalized, queryable, and pre-indexed fields in MongoDB collections to support fast server-side OData queries (e.g. `disposition_rank`, `company_name`, `has_score`, `has_application` on postings; `status`, `applied_at`, `company_id`, `posting_title`, `company_name`, `has_interview`, `first_response_at`, `last_event_at`, `has_first_response`, `has_last_event`, `has_offer`, `has_rejection`, `rejected_before_interview` on applications; `has_effective_to` on criteria).

When schema versions change, migrations occur, or data is restored from legacy snapshots, read models must be reconciled to backfill or re-sync these computed fields.

## Semantics & Boundaries

- **Derived Fields Only**: Reconciliation only writes derived/denormalized fields (`disposition_rank`, `company_name`, `has_score`, `has_application` for postings; `status`, `applied_at`, `company_id`, `posting_title`, `company_name`, `has_interview`, `first_response_at`, `last_event_at`, `has_first_response`, `has_last_event`, `has_offer`, `has_rejection`, `rejected_before_interview` for applications; `has_effective_to` for criteria). It does **not** modify `updated_at` timestamps on documents.
- **Collections Covered**: postings, applications and criteria. Companies, notes and search runs add no derived fields (their OData read models read the stored fields directly), so they need no backfill.
- **No Audit Entries**: Reconciliation is an operational backfill/maintenance procedure, not a domain action; it does **not** record audit trail entries.
- **Global Scope (Not Owner-Scoped)**: Reconciliation runs across all documents across all owners in the database to align read models globally.
- **Transactional Cascades**: Company renames and posting title changes automatically cascade transactionally across postings and applications at runtime. Reconciliation serves as the safe backfill mechanism for data imported before cascades were active or restored from legacy snapshots.

## Insights fields (feature 007)

The Insights dashboard and its drill-down filters rely on four derived fields added by feature 007. They are written only at the repository choke point (and here), and they are not user-editable:

| Collection | Field | Meaning |
| --- | --- | --- |
| postings | `has_application` | an application exists for the posting |
| applications | `has_offer` | any `offer` event |
| applications | `has_rejection` | any `rejected` event |
| applications | `rejected_before_interview` | a rejection with no `interview` event dated on or before the earliest rejection; false without a rejection |

(`first_seen_at` on postings is stored already and needs no backfill.)

Documents written before feature 007 lack these fields. Run the procedure below after deploying the feature. Until it has run, the Insights page shows "needs backfill" instead of numbers that depend on them, because a MongoDB filter such as `{ field: false }` does not match a document that lacks the field. The first dry run is expected to report every posting and every application as needing an update; the dry run after the confirmed run must report zero.

## Operational Rules & Safety

> [!IMPORTANT]
> **Operator Rule**: Matt runs `reconcile-read-models` on the real database **manually** following the exact run order below. Antigravity (agy), autonomous subagents, and automated CI/CD jobs **never** run this command against live production databases.

### How to run it on the real stack (Windows)

The bare `roleradar ...` commands below are not on your path, and a plain container has no route to the compose Mongo. These are the forms that work. Run them from PowerShell at the repository root.

- **Reconcile** runs in the `roleradar-cli:local` image on the compose network (`deploy_default`), where the hostname `mongo` resolves. A container started with no network option cannot see it: `127.0.0.1` inside the container is the container itself.
- **Backup** runs on the host through `scripts/backup.ps1`. That script starts the built CLI (`dotnet run --project src/RoleRadar.Cli --no-build -c Release`), and the CLI dumps the database with `docker compose -f deploy/docker-compose.yml exec -T mongo mongodump --archive --gzip --oplog` against the running `roleradar-mongo` container. It needs the Release build of the CLI to exist, `docker` on the path, and a passphrase.

Because the image is built from the checkout, **rebuild it first** whenever the code changed (a stale `roleradar-cli:local` has no `reconcile-read-models`, or an older version of it). This builds the image only and starts nothing:

```powershell
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps build cli
dotnet build src/RoleRadar.Cli -c Release
```

The wrapper `scripts/roleradar-docker.ps1` can start the same container. With no environment variables it adds no network and uses the test image `roleradar-cli:test`. Set these to point it at the real stack (the wrapper forwards the connection string and database name, and adds `--network` only when `ROLERADAR_DOCKER_NETWORK` is set):

```powershell
$env:ROLERADAR_DOCKER_NETWORK = "deploy_default"
$env:ROLERADAR_CLI_IMAGE = "roleradar-cli:local"
$env:ROLERADAR_MONGO_CONNECTION_STRING = "mongodb://mongo:27017/?directConnection=true"
$env:ROLERADAR_MONGO_DATABASE = "roleradar"
pwsh ./scripts/roleradar-docker.ps1 reconcile-read-models
```

The steps below use the plain `docker run` form, which needs nothing set beforehand and is the same command the wrapper builds. Everywhere you see `reconcile-read-models` you can use either.

### Recommended Run Order

1. **Step 1: Deploy API and Web**
   Deploy the updated API and web application so the new OData query endpoints and models are active, and rebuild the CLI image (see above).

2. **Step 2: Create an Encrypted Backup**
   Before running any reconciliation on a live environment, create an encrypted backup. The destination is a directory or a file path; the script asks for the passphrase securely, or reads it from an environment variable (`-PassphraseEnv BACKUP_PW`) or standard input (`-PassphraseStdin`). A passphrase on the command line is refused.

   ```powershell
   pwsh ./scripts/backup.ps1 <destination>
   ```

   Backups are only guaranteed consistent when nothing writes during the dump, so stop the web app and any MCP session first.

3. **Step 3: Execute Dry Run**
   Run the reconcile command without `--confirm` (dry-run mode). No documents are modified; the CLI scans the collection and reports the number of records examined and how many require updates:

   ```powershell
   docker run --rm --network deploy_default -e "ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27017/?directConnection=true" -e ROLERADAR_MONGO_DATABASE=roleradar roleradar-cli:local reconcile-read-models
   ```

   Inspect the summary output (`Postings Examined`, `Postings Updated`, `Applications Examined`, `Applications Updated`, `Criteria Examined`, `Criteria Updated`).

4. **Step 4: Execute Confirmed Reconciliation**
   Once the dry-run summary is verified, apply the updates by passing the explicit confirmation token:

   ```powershell
   docker run --rm --network deploy_default -e "ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27017/?directConnection=true" -e ROLERADAR_MONGO_DATABASE=roleradar roleradar-cli:local reconcile-read-models --confirm RECONCILE-READ-MODELS
   ```

5. **Step 5: Verify Idempotency (Dry Run Again)**
   Re-running the dry-run command immediately after confirmation must report `Postings Updated: 0`, `Applications Updated: 0` and `Criteria Updated: 0`:

   ```powershell
   docker run --rm --network deploy_default -e "ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27017/?directConnection=true" -e ROLERADAR_MONGO_DATABASE=roleradar roleradar-cli:local reconcile-read-models
   ```
