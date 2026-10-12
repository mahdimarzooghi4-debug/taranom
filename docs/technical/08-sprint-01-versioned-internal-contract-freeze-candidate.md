# TRN-TECH-009 — Sprint 01 versioned internal contract freeze candidate

- **Status: CONTRACT-FREEZE CANDIDATE / REVIEW REQUIRED — not Code Authorization, not implemented**
- Date: 2026-10-09
- Applies only to **synthetic non-Production fixtures** in the C#/.NET operational core.
- Parents: `TRN-TECH-003/005/006/007` and Business `TRN-BIZ-018…023/029/035/036/052`.
- **No HTTP, external IdP, national identity assertion, real legal consent, medical record, payment, insurance, Python worker, AI model or Production permission is created.**

## 1. Module contracts and trust boundary

The first Sprint is **internal strongly typed .NET**, not REST endpoints.

| Owner | Candidate contract v0 | Side effects | Explicit rejects |
|---|---|---|---|
| Identity & Access | `IAccessPolicyV0.Evaluate(TrustedSyntheticContext, AccessQueryV0)` | append safe audit of decision | client-claimed role, absent test-origin proof, cross-subject/scope |
| Consent & Privacy | `ISyntheticConsentStoreV0.Grant/Withdraw(...expectedVersion, requestRef)` | append evidence history + audit transaction | implicit research permission, stale version, mismatched subject |
| Consent & Privacy | `IConsentPolicyV0.Evaluate(TrustedSyntheticContext, ConsentQueryV0)` | read evidence + decision audit | missing evidence, wrong purpose/scope, withdrawal |
| Audit & Operations | `IAuditAppendOnlyV0.Append`, restricted test projection | insert-only evidence | update/delete, secrets/PHI, unauthorised read |
| Test harness, NOT runtime | `TrustedSyntheticContextFactory` used only in tests | generates clearly test-only identities | any Production assembly inclusion, config-only enabling |

`TrustedSyntheticContext` **must be server-owned and unconstructible from external/untrusted request claims**. Test actors are fixture identities only; never substitute them for real verified clinical identities. No cross-module repository writes; calls use owner contracts.

## 2. Exact contract shape candidates for review (no code signatures frozen yet)

Common references:
- `ActorRef`, `SubjectRef`, `ResourceRef`, `ScopeRef`, `EvidenceRef`, `CorrelationRef`, `RequestRef` and `VersionRef`: **opaque, nonempty, typed synthetic-only identifiers**. None embeds PII or a national identity number.
- `TrustedSyntheticContext`: constructed by test assembly, includes `testOrigin`, actor and scope evidence. Origin is a **capability enforced by build/deployment isolation**, not a client input string that can authorize Production.
- `AccessQueryV0`: subject, resource category/ref, action and scope. Result `ALLOW | DENY` with non-sensitive reason and `auditRef`. No implicit allow on missing/expired/stale evidence.
- `ConsentPurposeV0` is a **test-only set**: `CARE_COMMUNICATION_TEST`, `HEALTH_DATA_SHARE_TEST`, `SECONDARY_AI_RESEARCH_TEST`. These three never imply each other. `CostChoiceEvidence` from `TRN-BIZ-052` **is a separate type** and has no consent-grant method.
- `ConsentMutationV0`: trusted actor+subject, exact purpose/scope, source version, `expectedVersion`, stable `requestRef` and correlation. `Grant` creates an appended evidence record; `Withdraw` appends an ordered new record pointing to prior evidence. The associated audit is atomically committed in the same DB transaction.
- `ConsentQueryV0`: exact subject/purpose/scope and as-of **synthetic test clock**. Result `VALID | DENY` based on current nonwithdrawn history and exact evidence. No legal retention duration or time validity window is inferred.
- Typed result classes `INVALID_INPUT`, `UNTRUSTED_ORIGIN`, `NOT_FOUND`, `VERSION_CONFLICT`, `AUDIT_UNAVAILABLE`, `IDEMPOTENCY_CONFLICT`. For authorization and consent decision **any unhandled exception/failure is DENY**. These are **internal categories only**, not finalized HTTP codes.

**Versioning rule:** `V0` deliberately denotes **pre-review proposal**; this contract must become reviewed `V1` with an explicit owner, version compatibility policy and Code Authorization before implementation. `V0` must never be represented as an actual API.

## 3. Persistence and concurrency candidate

- Test persistence in **PostgreSQL 18.6** with **logical owner separation** for access test identities, consent event history, and audit; no unrestricted cross-owner mutation.
- Consent history: append-only evidence with unique `EvidenceRef`, chain `previousEvidenceRef`, exact subject/purpose/scope/sourceVersion and synthetic origin. Withdraw is **new evidence**, not deleting or mutating past history.
- State progression is protected using **aggregate optimistic version check inside one transaction** and DB constraints; two concurrent operations with the same expected version cannot both succeed. Do **not** assume EF concurrency token alone covers a missing aggregate, retries or distributed systems without DB test evidence.
- Idempotency: `(synthetic scope, actor, operation, requestRef)` is unique for **mutating commands only**; exact replay with identical normalized input returns the same effective result, changed input under the same key = `IDEMPOTENCY_CONFLICT`. Identical normalized input canonicalization and digest format require test vectors before code; no invented public idempotency API.
- Protected mutation and the corresponding audit record **must commit or rollback together**. If `AuditAppend` fails, no consent mutation may remain. Reject orphan events, partial success and nondeterministic concurrency outcomes.
- Audit append-only is enforced **at the database permission/constraint boundary** and verified with direct SQL attempts, not merely an EF repository method. Migration/DDL owner is separate from application actor and must be protected; no claim of tamper-proofness against DB admins.
- Synthetic audit has **allowlisted metadata only** (action, decision, non-identifying refs, timestamps/correlation). No tokens, national IDs, clinical notes, full invoice amounts, diagnosis text or raw request bodies in persisted audit/logging.
- Unknown values stay **unknown**, never default to allowed/approved/zero. No actual retention period, isolation level, DB schema naming, SQL type lengths, generated encryption key or credential is fixed by this candidate.

## 4. Module and environment isolation decision candidate

- Library-level .NET modules own Identity, Consent and Audit contracts; **test harness is a test-project-only dependency**, never referenced by runtime/API/deployable assemblies.
- A Production build/package check must prove that synthetic factories and seeded test authority are absent. An environment variable, feature flag, debug header or request payload **cannot turn test identities into trusted Production identities**.
- No external network service is needed for Sprint 01. A PostgreSQL **ephemeral test database** is allowed; network access for normal package restore and test container pull must be deliberate and pinned in CI.
- A missing real IdP, valid real consent, legal clinic/provider or insurance adapter is a **distinct external integration gate**, not a reason to invent local Production successes.

## 5. Traceability and acceptance

| Proposed contract | Acceptance scenario references |
|---|---|
| Access policy & synthetic origin | ID-01…08; X-05…07 |
| Independent consent & withdrawal | C-01…06; X-01…03 |
| Audit invariants & data minimization | A-01…05; X-03, X-04, X-08 |
| Absence of real providers and AI | B-01; X-06 |
| All scoped commands | negative/replay tests, transaction evidence and code review |

**Next acceptance work**: reviewers resolve the candidate's explicit remaining details (concrete DTO serialization if needed; DB schema and concurrency mechanism; canonical idempotency input; exact error versioning; audit/write isolation; package runner). Then and only then can `TRN-SP-01` obtain **explicit Code Authorization**. This candidate is not a signed-off, executable or Production-ready contract.
