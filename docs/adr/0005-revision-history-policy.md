# ADR 0005: Revision History Policy — Full Snapshots Only for Destructive Edits

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

Notes in RoleRadar can exceed 100 KB (up to 1 MB) and receive frequent `append` operations from an AI agent during research sessions, as well as occasional `replace-section` and `import-overwrite` operations. We must guarantee that no prior text is ever lost while avoiding quadratic storage amplification.

When should a full prior-body `NoteRevision` document be written to `note_revisions`?

## Decision Drivers

- **Zero Text Loss (`FR-008`)**: Any write that overwrites or removes existing text must preserve the complete prior markdown body.
- **Linear Storage Scaling on Appends (`FR-004`, `FR-008`)**: Because `append` strictly concatenates text to the end of the existing body, the old body is always an exact prefix of the new body. Writing a full-body snapshot on every `append` to a 100 KB+ note would consume $O(N^2)$ bytes across $N$ appends.
- **Concurrency Safety & Typed Results (`FR-004`, `FR-005`, Constitution v1.1.0 Principle IV)**: `AppendToNote` executes atomically server-side without a caller version token, whereas `ReplaceNoteSection` requires optimistic concurrency (`version_token`, returning typed `ErrorCode.StaleVersionToken` on mismatch) and `ImportMarkdown` overwrite verifies `current_body_hash == last_imported_body_hash` (returning typed `ErrorCode.ImportConflict` on mismatch).

## Considered Options

1. **Full prior-body `NoteRevision` snapshots only on `ReplaceNoteSection` and `ImportMarkdown` overwrite; `AppendToNote` records only `appended_text` and `resulting_version_token` in `audit_log`**.
2. **Full prior-body `NoteRevision` snapshots on all writes including `AppendToNote`**.
3. **Delta/diff patches for all writes**.

## Decision Outcome

Chosen option: **Option 1 — Full prior-body `NoteRevision` snapshots only on `ReplaceNoteSection` and `ImportMarkdown` overwrite**, while `AppendToNote` executes atomically server-side, writes no `NoteRevision`, and records `appended_text` plus `resulting_version_token` in `audit_log`.

### Consequences

- **Good**: Storage grows linearly ($O(N)$) across repeated appends to large notes while keeping 100% of historical text recoverable (since prior append states are prefixes of the current body or the next destructive revision's `prior_body`).
- **Good**: Destructive edits (`ReplaceNoteSection` and `ImportMarkdown` overwrite) always capture a complete, self-contained prior markdown body in `note_revisions` linked via `audit_log.note_revision_id`.
- **Bad / Trade-off**: Reconstructing the exact body state between two historical appends requires slicing the body at the appended text boundary recorded in `audit_log` rather than reading a standalone `note_revisions` row.
