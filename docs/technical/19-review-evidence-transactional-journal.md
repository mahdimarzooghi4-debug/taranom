# TRN-TECH-019 — Atomic append-only evidence-review intake journal (isolated PostgreSQL)

**Date:** 2026-10-10. **State:** implemented only for synthetic PostgreSQL 18 test databases in a separate non-packable internal .NET project. Business decisions: TRN-BIZ-053/054 (one clinical center with departments, no network parent); TRN-BIZ-043/044 (contract and independent medical qualifications remain separate). Previous pure evidence intake: TRN-TECH-018, Draft PR #16.

## Purpose / explicit limits

Persist **unverified evidence claims and pending-review work items** so retries, races, audit write failure and historical tampering can be tested before any real identity/provider integration. The only supported subjects remain a center representative's manager-authority claim, a physician's professional-qualification claim, and a physician's valid Taranom service-contract claim. The latter two are distinct evidence categories. The claim submitter, opaque evidence reference, doctor and center are synthetic `test:` strings; they are NOT legal source proofs or verified owners. No uploaded documents, private health information, verified physician, contracts, user identity or runtime permissions are stored or inferred.

New separate `Taranom.ClinicalEvidenceJournal.Postgres` Npgsql adapter and companion genuine PostgreSQL integration tests. The public web panels, protected-data interlock and live apps **do not reference** this test-only assembly. It contains no HTTP/OIDC/clinical credentials or Production migration.

## Relational test schema and transaction

- `internal_test_review_case`: composite key `(center_ref, subject_ref, subject_kind)` with monotonic accepted revision; one logical history per specific center/subject/type, including separate representative vs physician cases. Scoped IDs alone are NOT authoritative tenant authentication.
- `internal_test_review_event`: immutable accepted submitted command JSON, request UUID, revision and SHA-256 digest; uniqueness by history+revision and history+request UUID.
- `internal_test_review_work_item`: only the pending review category, claimant reference and opaque reference; one-to-one composite FK to an accepted event. **No fields to mark the item approved, verified, rejected, licensed, active or payable.**
- `internal_test_review_audit`: one-to-one FK to accepted event, accepted request UUID and digest in the same SQL transaction as event, item and current revision update.
- `internal_test_review_denial`: minimal append-only rejection ledger with reason, seen version, synthetic identifiers and request UUID; it does not include evidence content or real records.

A write creates or locates its case row, locks the exact center/subject/kind with `SELECT FOR UPDATE`, then **rehydrates all accepted events through the independent TRN-TECH-018 deterministic pure reducer** and verifies paired work-item fields, paired accepted audit, SHA-256 digest, chronological revisions and current revision. Divergence throws and fails closed. Accepted claim → event + pending review item + accepted audit + compare-and-swap current revision + **single COMMIT**. Any audit failure rolls everything back. Exact historical replay produces no new event/review/denial; conflicting replay, stale version, incompatible kind or wrong case produces a denial (provisional case creation is rolled back). If denial audit cannot write, fail by throwing rather than returning a silently unrecorded rejection.

Append-only triggers reject UPDATE/DELETE on accepted event, review work-item and audit and denial tables even for ordinary SQL owner DML. This **does not** resist superuser trigger disabling, schema ownership tampering, test credentials disclosure or PostgreSQL administrator privilege. SHA-256 without a secret/external anchoring is a consistency check, not tamper-proof attestation.

## Acceptance / exclusions

Independent real PostgreSQL test job covers durable rehydration, case/multi-center scope, professional-vs-contract work items, row-lock concurrent submission, same-ID retry, changed retry, stale version, unrelated subjects, injection of accepted/denial audit failure, history-protection triggers and corrupted revision read/write prevention. Inherit 11 earlier CI jobs including Member and Center Chromium and previously implemented PostgreSQL suites (12 independent jobs total). No accepted review decision, verified issuer, delegation, approved medical/contract status, API, file storage, real actor, authorization, manager activation, physician activation, insurance or payment.

**Remaining real admission blockers:** approved official ID/center registry, legally authoritative professional-verification integration, valid Taranom contract/provenance registry, source-owned and legally reviewed permissions, privacy/retention, independent human review rules/appeal and credible audit identity. Do not invent a verifier, service or regulatory criterion, use `Confirmed` as authority, or wire this test journal to the real center panel. PR Draft/Open; no independent Code Review/Stage/QA/Release/Production.
