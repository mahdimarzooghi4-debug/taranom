# TRN-TECH-029 — Historical synthetic receipt replay provenance fence

**Date:** 2026-10-10. **Status:** isolated, non-deployable TEST ONLY. Parent PR #26 and TRN-TECH-028. TRN-BIZ-043/044/053/054 unchanged. No medical, license, clinical-access, reviewer-rights or conflict-of-interest decision.

## Threat

The Sprint 19 history inspector correctly returned `HistoricalIssuerVersionDifferent` when a TEST DOUBLE claimed a different issuer identity or revision from the immutable receipt. However, `PgSyntheticPacketReceiptJournal.RecordAsync` could still return `ExactReplay` for the same command whenever its first synthetic check passed, despite the current claim using another simulated issuer/version. That overstates what the duplicate receipt observation means and permits an internal caller to lose a useful distinction.

## Narrow technical contract

1. Keep the existing initial `ConflictPacketReceiptGuard.InspectAsync` as the source-failure/identity/claim gate. Missing, revoked, unavailable, malformed or request-mismatched fake source returns the existing nonauthorizing failure before touching replay state.
2. Under the existing transaction and per-request PostgreSQL advisory lock, retrieve the original row through `ReadLockedAsync` including paired audit and v2 SHA-256 consistency checks. Corruption remains a fail-closed exception, not a successful replay.
3. For a **different command using an already accepted request ID**, `ConflictingReplay` takes precedence regardless of issuer claim; no overwrite or second insert.
4. For an exact command, compare `SimulatedIssuerRef` and `SimulatedIssuerRevision` from the synthetic source's positive first check to the stored historical pair. Equal values allow the existing `ExactReplay` **historical technical result only**. Unequal values return the new explicit `HistoricalIssuerVersionDifferentOnReplay` (non-authorizing).
5. Both replay paths roll back the read transaction. The original receipt and paired audit remain intact. No new table, secondary journal, update of history, acceptance of real permission, clinical access, API or UI is permitted.
6. Parallel same-request replays remain serialized by existing database locks; new tests assert they cannot add rows or paired audit.

## Boundaries and known limitations

A syntactically positive TEST DOUBLE can lie. A single source check for a historical replay cannot guarantee latest/monotonic revocation or atomic external authority state. A malicious source could return A→B→A, or alter after the check. Even `ExactReplay` is only an historical idempotency observation, never evidence of a human recipient's authorization, legal acknowledgement or current rights. Ordinary SHA-256 and DB triggers are NOT signatures or tamper protection against privileged DB owners. Sprint 08 protected-data gate remains DENY-ONLY.

No genuine identity/credential/issuer/assignment/revocation provider, trust anchor, medical reviewer, production adapter, consent grant, payment, patient record or doctor activation is added. Contract for trusted reviewer identity, external source revisions, revocation ordering/atomicity, authorized human review and privacy rights remains undecided; independent security, legal and clinical review is required before any real integration.

## Acceptance criteria

Real PostgreSQL integration tests must cover issuer-ref replacement, issuer-revision change, historical row/audit unchanged, changed-command precedence, revoked/missing source denial on replay, concurrent changed-issuer retry and unchanged historical replay. Run the entire inherited independent 12-job pipeline including isolated PostgreSQL evidence tests, Member and Center browser suites, and DENY-ONLY regressions. No PR Ready/Merge/Stage/QA Gate/Release/Production without explicit authorization.
