# TRN-TECH-015 — Transactional Clinical Center Journal (test-only PostgreSQL)

Date 2026-10-10. Status: implementable isolated technical slice. Business contract is TRN-BIZ-053/054 from Draft PR #10. Previous code slices: Draft PR #11 pure .NET center reducer and Draft PR #12 disconnected RTL center management UI.

## Model
**One Center → many Departments; a center may record its unverified DoctorAffiliations and doctor-to-department assignments.** A chain hospital is a single center with departments. No parent network / collaborating-network role or panel; no cross-center operations. A separate center has a separate aggregate and SQL composite keys.

This slice adds `Taranom.ClinicalCenterJournal.Postgres`, an **internal, nondeployable, synthetic-only** Npgsql adapter and ephemeral isolated PostgreSQL 18 schema. Project assembly is accessible only to its companion integration tests and it is not referenced by any server or browser bundle.

Versioned transactional tables in *isolated test DB*: `internal_test_center` row-locked aggregate identity/revision, `internal_test_center_department`, `internal_test_center_doctor` (always PendingIndependentVerification or Ended), `internal_test_center_assignment` with same-center composite FK, `internal_test_center_event` unique (center, revision) and (center, request), `internal_test_center_audit` paired per accepted event, and append-only `internal_test_center_denial` for rejected reducer commands. No clinical/name/national-ID/provider contract, license, insurer, wallet or patient fields.

## Transaction semantics
1. Check synthetic center reference (reject real/pathlike identifiers), transactionally insert center if necessary and **SELECT ... FOR UPDATE** its current version.
2. Rehydrate the full accepted event stream through the independent pure `ClinicalCenterJournal.Apply` reducer, checking per-event SHA-256 integrity and matching accepted audit. Assert replayed version and relational current projections match. Fail closed on detected divergence. SHA-256 is a corruption check, **not** a secret-keyed signature that can resist superuser modification.
3. An exact historical replay is returned as Replayed with **no new writes**. Changed payload under the same center/request key is a conflict. Different concurrent writers at the same expected version serialize: one accepted, the other StaleRevision.
4. For an accepted command, mutate only the corresponding relational projection, append one event and its audit record, CAS the current revision and COMMIT **all in one DB transaction**. A forced audit failure rolls back every mutation.
5. For deterministic denied commands, roll back provisional center creation before appending a minimal denied-attempt audit record in a separate committed SQL statement; if denial auditing fails, throw rather than return a silent unlogged rejection. Denied audit stores only random attempt ID, synthetic center ref, request ID, reason and observed version; exact replay does not count as denial.
6. SQL role used by tests can INSERT/SELECT accepted audit/event/denied ledgers, cannot DELETE/UPDATE them. Append-only triggers also reject ordinary schema-owner UPDATE/DELETE. The same runtime role has necessary projection mutations. **No claim** of protection from a database owner/superuser or production-ready RLS/tenant access.

## Explicit non-goals / security gates
- The center-manager-authority and audit-availability fields are still **synthetic, caller supplied**; they can be forged. This is not an authentication integration, legal permission, insurer/provider verification or an approved center read/write API.
- There are no real clinician identity, physician certificate/license, clinical data, production migration, deployment, retention policy, monitoring, response to compromised principals, or NATS event publisher.
- The immutable pure reducer's `Applied` affiliation is only `PendingIndependentVerification`: even if a record is durable in a test DB it cannot activate medical work, provider payments or patient access.
- The store currently validates replay by scanning complete accepted history under a row lock; long-running / large-center workloads would require a production-ready bounded, benchmarked design, not a guessed performance threshold.
- Valid synthetic reference prefix alone is NOT tenant authorization. Cross-center write protection in this exercise is logical and relational, not a substitute for attested actor-to-center mapping, RLS, cryptographically authenticated sessions and independent security/clinical reviews.

## Acceptance
18 genuine PostgreSQL integration tests (not mocking SQL) covering multi-department doctor assignments, cross-center scoping, exact/conflicting replay, concurrency, denial audit, fail-closed audit failure, privilege checks, privileged tampering, invalid identifiers, end affiliation, and durable rehydration. Preserve inherited 6 independent Sprint01–05 jobs including real Chromium member/center bundles. CI green is Code evidence, never Stage, formal QA or production permission.
