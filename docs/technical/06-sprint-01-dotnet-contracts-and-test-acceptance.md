# TRN-TECH-007 — Sprint 01 .NET Internal Contracts & Executable Acceptance Design

- **Status: TECHNICAL DRAFT / TEST SPECIFICATION, NOT IMPLEMENTED OR CODE-AUTHORIZED**
- Date: 2026-10-09
- Traceability: `TRN-TECH-003/005/006`, `TRN-BIZ-018…023, 029, 034…036, 052`.
- Technology target: synthetic-only .NET 10 module boundaries + PostgreSQL 18.6 integration tests, **no Production authentication or medical data**.

## 1. Invocation/transport boundary

- Use **internal strongly typed .NET service contracts** for Sprint 01; do not create public-facing medical, identity-verification, wallet or consent endpoints. Any testing ingress must be **test-project-owned, nonproduction-only** and compiled/deployed without cross-boundary access in Production. ASP.NET Controller/minimal API route shapes are **not approved** here.
- Every invocation carries an **identity assertion from a trusted server-side test harness only**; fields received from an untrusted client must never grant actor identity or roles.
- Result categories for test-only internal contracts: `ALLOW`, `DENY` for access; `VALID`, `DENY` for synthetic consent; typed failure categories `INVALID_INPUT`, `UNTRUSTED_ORIGIN`, `NOT_FOUND`, `VERSION_CONFLICT`, `AUDIT_UNAVAILABLE` for rejection/reporting. Exact HTTP status codes are **deferred** until a reviewed HTTP contract exists. No failure state is silently translated to ALLOW.
- Missing/ambiguous scope, stale evidence, mismatch subject/purpose/tenant, clock uncertainty for evidence validity, and absent audit guarantees **fail closed**.

## 2. Proposed typed operations

| Contract | Inputs (conceptual, synthetic) | Output | Invariant |
|---|---|---|---|
| `EvaluateAccess` | trusted ActorRef, SubjectRef, ResourceRef, Action, ScopeRef, Origin, EvidenceVersion, CorrelationRef | AccessDecision + reason + audit ref | default DENY; subject/tenant/ownership must match |
| `RecordSyntheticConsent` | ActorRef, SubjectRef, Purpose, Scope, SourceVersion, Origin, CorrelationRef, expected prior version | append-only evidence reference/version | no implicit consent across purposes; atomic audit |
| `EvaluateSyntheticConsent` | trusted subject, purpose, scope, evidence version, effective context | allow/deny + evidence ref | unknown/missing/stale => DENY |
| `WithdrawSyntheticConsent` | actor, consent ref, expected version, correlation ref | new immutable history record | previous history not updated/deleted; concurrent stale change rejects |
| `AppendAudit` | actor ref, action/decision, resource category, evidence ref, correlation ref, test origin | immutable audit reference | no PHI/PII/secrets in payload |
| `ReadAuditTestOnly` | authorized test observer, limited filter, scope | audit projection | no public read or cross-subject leakage |

Field names and enum values are **contract candidates for testing**, not approved legal consent records or public API. Identity verification provider and consent effect in real care are excluded.

## 3. PostgreSQL proof obligations (not final migration/schema)

- Use **separate logical owners** for access/test actor facts, test consent evidence/history and audit; do not share unrestricted write repositories.
- `Audit` records are insert-only, with database-enforced update/delete denial (e.g. roles/triggers after design review). API-level rejection alone is insufficient. Administrative DDL and backup/recovery are distinct governed operations; do not misrepresent PostgreSQL as physically immutable.
- For protected mutation plus audit, persist consent history and matching audit in **one database transaction** or abort both; if later architecture crosses DB boundaries, a separately reviewed atomicity/outbox contract is required.
- Uniqueness for synthetic `event_ref` and request idempotency scope (actor + operation + stable request identifier) must be DB-constrained, replay-safe and independently tested under concurrent workers; same key with changed payload **must reject**, never silently reuse. Exact public idempotency API is OPEN.
- Consent withdrawal is **new append-only history**, not a `DELETE` of evidence; verify stale expected version blocks competing updates.
- Audit records refer to synthetic non-identifying keys; no real national ID, clinical note, access token, bill body or actual patient identifier in traces/logs.
- Database schema names, column lengths, SQL isolation level, audit retention duration and partition strategy are **not yet approved**; decide only with observable test outcomes and privacy/legal review.

## 4. Acceptance evidence matrix

The 20 cases from `TRN-TECH-003` are mandatory, but not the only tests:

| IDs | Evidence class | Required result |
|---|---|---|
| ID-01…08 | .NET policy unit/contract/security | DENY by default; explicit scoped ALLOW; sponsor/cross-tenant/untrusted client rejected; synthetic authority impossible in Production |
| C-01…06 | .NET consent unit/DB tests | independent purposes, match subject/scope/version; atomic audited grant/withdraw; fee-choice is not legal consent |
| A-01…05 | PostgreSQL integration/concurrency | insert-only audit enforced at DB; rollback atomicity; no PHI/secret leakage; replay no duplicate effect |
| B-01 | Nonproduction isolation/security | real identity, wallet, insurance, clinical data and AI operation entirely unsupported/fail-closed |

**Required additional negative/concurrency tests beyond the original 20:**
- `X-01`: two concurrent consent version updates: exactly one accepted; other is `VERSION_CONFLICT`; no orphan audit/state.
- `X-02`: same idempotency identifier with different payload: reject without mutation.
- `X-03`: DB audit write failure midway: consent mutation rolls back; no success response.
- `X-04`: privileged test role cannot `UPDATE` or `DELETE` persisted audit via direct SQL.
- `X-05`: revoked/changed scope invalidates subsequent synthetic decisions; stale snapshot never permits access.
- `X-06`: synthetic harness assembly/binding missing or disabled in non-test configuration; no permissive fallback.
- `X-07`: attempt to pass sponsor-owned identity as member/clinician is denied even if tenant belongs to same sponsor.
- `X-08`: attempt to log a token, national identifier or text of health record fails the log/audit sanitization gate.

All cases must pass across database migrations on a fresh isolated PostgreSQL instance. This is **design acceptance only**: no CI or tests have been executed.

## 5. CI/Code Review planned order

1. `restore --locked-mode` using pinned SDK/packages and dependency vulnerability audit; validate supply chain.
2. `format --verify-no-changes`, analyzers/warnings-as-errors and `build`.
3. Unit + contract/security fixtures on synthetic inputs; ensure no real credentials.
4. PostgreSQL-backed migrations + concurrent access, consent and append-only audit tests, including negative cases `X-01…08`.
5. Fail-closed deployment bundle inspection: test harness absent in release/Production configuration.
6. Code Review for ownership/permission/privacy, mutation+audit atomicity, concurrency and traceability, then only later Stage/QA with explicit approval.

No actual YAML workflow, database schema, production endpoints or secret definitions should be committed before relevant contract review and Code Authorization. Do not label the above steps 'CI green' until an actual run for the exact commit succeeds.

## Review blockers

- Explicit approval of .NET 10.0.401 / EF 10.0.12 / Npgsql 10.0.3 / PostgreSQL 18.6 candidate and exact test runner/dependency lock.
- Actual versioned service contract/DTO, null/error/version/idempotency definitions after review; prototype solely with synthetic fixtures.
- Test container digest/credentials, migration plan and DB-level immutability proof.
- Code Authorization, CI and independent Code Review evidence. **No Stage/QA/Release/Production**.
