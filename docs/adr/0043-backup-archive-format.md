# ADR 0043: The Backup Archive Format Stays (`.rrbak`, libsodium secretstream and Argon2id)

- **Status**: Accepted (2026-11-11)
- **Date**: 2026-11-11
- **Feature**: `014-standards-first` (story P4, Jira RR-82)

## Context and Problem Statement

Principle X (Standards First) asks whether a standard or a maintained tool already does the job of a hand-written mechanism. The mechanism here is the backup archive: `SecretStreamBackupArchiveService` (1,420 lines in `src/RoleRadar.Infrastructure/Backup`) streams `mongodump --archive --gzip --oplog` into an encrypted `.rrbak` file, and restores it in two passes (ADR 0007). About 406 of those lines (lines 40 to 445: encrypt, Pass 1 verify, Pass 2 apply, `DecryptStreamAsync`) plus the 28-line `BackupArchiveConstants.cs` are the format. The rest is process orchestration (spawning `mongodump` and `mongorestore`, partial files, signals), the restore guards, source-database detection and wipe.

Candidates: `age` (an open format and tool; passphrase mode uses scrypt), `mongodump | age`, and `restic` (an encrypted deduplicating repository, a different shape of answer). The expected outcome was **keep as is**, unless `age` meets every criterion including restoring archives made by the current format. This record changes nothing: no source, script or format.

## The current format (criteria sheet)

Read from ADR 0007, the service, `BackupOptions.cs`, `scripts/backup.ps1` and `scripts/restore.ps1`.

| Property | Current format |
| --- | --- |
| Container framing | 54-byte header (`RRBK`, version byte `1`, flags `0`, Argon2 iterations, Argon2 memory in KiB, 16-byte salt, 24-byte secretstream header), then frames of `[int32 length][ciphertext + 17-byte tag]`. The header is authenticated as associated data on chunk 0. |
| Key derivation | Argon2id (RFC 9106 family), default 4 iterations and 256 MiB, stored in the header. The reader refuses more than 10 iterations or 1 GiB before deriving anything. |
| Chunking | 64 KiB plaintext chunks with one chunk of lookahead; the last chunk carries the `Final` flag; a 0-byte input still writes a final chunk. |
| Tamper and truncation | Flipped, dropped, reordered or truncated chunks fail authentication; a missing `Final` flag or trailing bytes after it fail Pass 1. All failures return one generic error ("Archive is invalid or corrupted"). |
| Passphrase flow | Interactive prompt, `--passphrase-env`, or `--passphrase-stdin`; a passphrase on the command line is refused by both scripts. |
| Restore verification | Two passes: Pass 1 decrypts everything into a scratch buffer and checks the `Final` flag before `mongorestore` starts; Pass 2 streams into `mongorestore`; then indexes are rebuilt and per-collection counts are returned. The target must be empty unless `--force-overwrite` is given. |
| Specification | ADR 0007 and the code only. The ADR argues that any libsodium binding can read it (not tested here). |

## How the spike ran

Everything ran in the scratchpad, outside the repository, against one Testcontainers MongoDB (`mongo:9.0.4`, the image `MongoTestImage` reads from the compose file, Testcontainers 4.16.0) on a random port (65345). The service was a copy exported with `git archive` from commit `fa57e685a`, driven by a throwaway console project; nothing was built inside the repository. Every harness command re-checked that the connection string, the Docker container name and the published port named the scratch container, and refused port 27017, `roleradar-mongo`, any non-loopback host and any database not prefixed `rr_g4c_`. The guard was tested with three bad states and refused all three.

Data was generated and fictional: 50,000 postings (96.1 MiB in MongoDB, 34.2 MB as a gzip dump) with invented companies, plus 400 companies, 600 applications, 3,000 audit entries and 150 notes. Content hashes are SHA-256 over the raw BSON of each collection sorted by `_id`; every restore below was into a scratch database and compared with the source (five collections, all counts and hashes equal). The real tools (`scripts/backup.ps1`, `scripts/restore.ps1`, `roleradar backup`, `roleradar restore`) were never run. The dump command is the service default (`mongodump --archive --gzip --oplog`), run inside the scratch container.

Tool versions: `age` v1.3.2, published 2026-08-29 (74 days old; the newest release at least 14 days old; its Windows zip matched the SHA-256 digest GitHub publishes for the asset, `f48d8f8f...e8c7`; the `.proof` files were not verified). `restic` 0.19.1, published 2026-07-05 (its zip matched `SHA256SUMS`; the `SHA256SUMS.asc` signature was not verified). `mongodump` 100.20.0 from the scratch image.

## Measured results

One 50,000-posting set, three runs where marked, same instance state for every backup.

| | Current format | `mongodump \| age` | `restic` |
| --- | --- | --- | --- |
| Archive size, bytes (gzip dump is 34,179,xxx) | 34,188,744 / 34,190,276 / 34,190,263 | 34,189,942 / 34,188,465 / 34,187,924 | 34,185,744 (whole repository) |
| Backup time | 1.61 to 1.72 s | 1.55 to 1.69 s | 1.95 to 2.61 s |
| Peak memory, backup | 301 MiB (whole .NET process; Argon2id 256 MiB) | 264 MiB (the `age-plugin-batchpass` process, scrypt 256 MiB; `age` itself 8 MiB) | 183 to 188 MiB |
| Restore into a scratch database | 3.67 s including Pass 1, index rebuild and counts; hashes equal | 2.31 s; hashes equal | 2.47 s; hashes equal |
| Wrong passphrase | 0.57 s, generic error, nothing restored | 0.49 s, "incorrect passphrase", 0 bytes released | 0.54 s, "wrong password or no key found", exit 12 |
| One flipped byte, header | Rejected in Pass 1, target untouched | Rejected, 0 bytes released | Not applicable (repository files) |
| One flipped byte, middle or last | Rejected in Pass 1 (0.6 s), target untouched | Rejected, but 17.0 MB (middle) and 34.1 MB (last byte) of plaintext were released first | `check --read-data` fails; a restore released 13.8 MB, then failed |
| Truncated (last 100 bytes, half) | Both rejected in Pass 1, target untouched | Both rejected, after 34.1 MB and 17.0 MB were released | Last 100 bytes of a pack (its header): `check` fails, restore still succeeded; half a pack: both fail, restore after 10.2 MB released |
| Partial restore if piped straight into `mongorestore` | Not possible (Pass 1 gates it) | Flip at 90 percent left **44,916 of 50,000** postings in the target | Same property as age |
| Archive made by the current format | Restores (the fixture, hashes equal) | **No.** `age -d` stops at the header: "unexpected intro: RRBK...". Decrypting with the shipped service and piping into `age` works (0.59 s), and the converted file restored with equal hashes, but that needs the current decrypt code | **No.** Only storable as opaque bytes |

Notes on the table:

- Both candidates add about 8.5 KB (0.025 percent) over the dump, the same as the current format. Neither is smaller in practice, because the compression is `mongodump --gzip`.
- The memory figures are dominated by the key derivation: Argon2id at 256 MiB against scrypt at work factor 18 (256 MiB). The streaming part of `age` is about 8 MiB. Matching the cost of the current default is not an advantage.
- `age` releases plaintext before it finds a bad chunk, as any streaming decryptor must. The current service answers this with Pass 1. With `age` the same guarantee needs a wrapper that decrypts twice (the exit code of a first run into a null sink gates the second); the pieces exist, but the wrapper does not.
- `restic` did not deduplicate here: a second backup of unchanged data grew the repository from 34.19 MB to 68.37 MB, because the `--gzip` dump is a different byte stream each run. Deduplication would need an uncompressed dump. Its restore path verifies blobs as it reads them; whole-repository verification is a separate command (`check --read-data`).
- Single runs only for restore times, so treat those as order of magnitude.

## Criteria

| Criterion | Current format | `age` / `mongodump \| age` | Verdict |
| --- | --- | --- | --- |
| Format stability | Version byte, specified only in ADR 0007 and the code; one reader | Public specification at [C2SP age](https://github.com/C2SP/C2SP/blob/main/age.md); 1.x releases 2022-12 (1.1.1), 2024-06 (1.2.0), 2025-12 (1.3.0, 1.3.1), 2026-08 (1.3.2) | `age` better |
| Audit history | libsodium: [Private Internet Access assessment of v1.0.12 and v1.0.13](https://www.privateinternetaccess.com/blog/libsodium-audit-results/) (August 2017, Dr. Matthew Green); I could not confirm secretstream was in scope. Geralt: none found | None found for `age` (web search 2026-11-11; the hits were scanner pages and downstream advisories, not an audit) | No difference |
| Streaming of large archives | Constant buffers, tested | Constant buffers, measured 8 MiB for `age` | Tie |
| Passphrase handling | Env, stdin or prompt, one binary | `age -p` waits on a terminal; with piped input it hung until killed at 15 s. Scripted use needs the separate `age-plugin-batchpass` binary on `PATH` and the passphrase in `AGE_PASSPHRASE` (or a file descriptor); its own help says most users should not use it. Decrypt accepts a work factor up to 22, which is 4 GiB of scrypt memory, against 1 GiB here | Current better for this use |
| Restore verification | Two-pass gate, indexes, per-collection counts | Verification of one pass only; the gate and the counts would be rewritten around it | Current better |
| Windows and container support | .NET only; works in the chiseled CLI image | Windows and Linux binaries exist. The CLI image is chiseled and has no shell, so `mongodump \| age` cannot be a shell pipe; the .NET service would have to spawn both (as it spawns `mongodump` today), and add two pinned binaries to the image | Tie, with more to maintain |
| **Migration of existing archives** | Restorable | **Not restorable by `age`**; a conversion works only through the current decrypt code | **`age` fails the stated rule** |

## Decision Outcome

**Keep the current format. No package, script or format change.** `age` is better on one criterion (a public specification), ties on three, is worse on two, and fails the one that was set as a condition: it cannot restore an archive made by the current format. The conversion path works, which is useful to know, but it keeps roughly 400 lines of the current crypto alive until every old archive is converted, so it removes none of what this card set out to remove. The format is also not the part of the service that carries the maintenance cost; the process orchestration and restore guards would stay either way.

`restic` answers a different question. It would turn "one encrypted file at a destination" (what `scripts/backup.ps1` promises) into a repository with retention and pruning, and deduplication does not help a gzip stream. It does not read the current format. Not pursued.

### If the owner wants to move to `age` anyway

This is a proposal, not a plan, and any change to the backup format is a separate decision that needs the owner's approval. A migration would need, at least: new archives as `.age` written by a spawned `age` and `age-plugin-batchpass` (both pinned by checksum in the `Dockerfile`, as the Mongo tools are); a reader that still opens `.rrbak` for as long as any exists, or a one-time conversion of each (proved to work above); a two-pass decrypt wrapper for restore; the passphrase passed by environment or descriptor, with the tradeoffs above; a decrypt-side work-factor ceiling set below age's default; and the existing backup and restore integration tests ported before the old code is touched. Nothing here should be run against the real database by an agent; the owner runs any real-data check.

### Revisit when

- `age` builds non-interactive passphrase use into the core tool, or publishes an audit.
- Geralt is unmaintained or libsodium secretstream gets a security advisory, since the format's readability by other libsodium bindings (ADR 0007) is the exit route.
- The owner decides that a public format specification outweighs the cost of keeping a reader for `.rrbak`.

## Spike notes

- The first seeded data collided with the service's unique indexes (`ux_companies_owner_normalized_name`); the first restore reported that, with the data itself equal. The seed was fixed to satisfy the real indexes and everything was rerun.
- The first `age` numbers came from a dump that also held restored scratch copies (twice the data). They were discarded; every figure above is from a rerun on one clean instance state.
- Scratch archives, repositories and the downloaded binaries were deleted afterwards, and the repository working tree held only this record, its index rows and the task ticks.
