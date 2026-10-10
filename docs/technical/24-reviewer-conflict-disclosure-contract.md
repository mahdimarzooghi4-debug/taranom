# TRN-TECH-024 — Potential Reviewer Conflict Disclosure Foundation (Synthetic Only)

**Date:** 2026-10-10. **Status:** internal, synthetic-only, intentionally non-authorizing. Parent: Draft Sprint14 PR #21 and TRN-TECH-023; Business decisions TRN-BIZ-043/044/053/054.

## Why this is necessary

The last Sprint checks only exact reviewer equality with the original claimant or subject. A different individual can still have an employment, family, supervisory, commercial, institutional or undisclosed relationship. **The product has NOT defined a complete conflicts-of-interest policy, official reviewer roles, permissible/impermissible relationship categories, disclosure obligations, recusal criteria, independent verification source, evidence retention or appeal model. None may be invented.**

This foundation supports one bounded technical action: record a **potential relationship disclosure** as unverified, synthetic reference material pinned to an existing, append-only **human review note** and its exact center, reviewed subject/type and evidence revision. This is not a reviewer determination or a finding of wrongdoing. It does not invent types/severity/thresholds for relationships or declare the submitter truthful.

## Implementation and data consistency

`PgReviewerConflictDisclosureJournal` is an INTERNAL/test-only Npgsql class within the existing isolated PostgreSQL bounded context. `ConflictNoticeCommand` binds request UUID, expected disclosure revision, center, subject, subject kind, exact accepted evidence revision, reviewed note's original request UUID, claimed reporter and opaque potential-relationship evidence reference. All claimant/data refs must be syntactically `test:` and are **untrusted**, with no real IDs/documents. The older human-review note journal must rehydrate and verify accepted note + audit history before recording a disclosure.

Three relational synthetic test tables maintain:
- `internal_test_conflict_notice_head`: exact-scope, row-locked current revision with composite FK to the immutable source human-note `request_ref`;
- `internal_test_conflict_notice_event`: append-only accepted JSON, request ID, revision, SHA-256 consistency digest and uniqueness for revision and retries;
- `internal_test_conflict_notice_audit`: paired accepted audit FK with identical hash and request ID in the **same transaction**.

Two concurrent writers cannot create different version-1 notices; an exact historical replay is idempotent and a changed replay conflicts; an outdated revision cannot overwrite history. Ordinary SQL UPDATE/DELETE attempts on accepted event/audit rows are rejected. A failed accepted-audit write rolls back provisional head and event. Read replays and validates the complete accepted notice/audit versioned history and digest, failing closed on inconsistency.

**Semantics are intentionally conservative.** An empty disclosure history returns `UnknownWithoutDisclosureEvidence`, **never "NoConflict", "Cleared" or "Approved"**. A recorded potential relationship returns `PotentialRelationshipRequiresIndependentReview`, **not verified misconduct, legal recusal or an authorized reviewer verdict**. A malicious caller can record false or incomplete synthetic notices. There is no decision to remove/delete/clear disclosures, no matching severity rule, deadline, override, credential, threshold, legal authority or clinical/financial workflow transition.

`PgReviewerConflictAdmission` is a separate internal read gate: it first checks the existing, deliberately source-unimplemented `PgReviewerProvenanceInterlock`. If reviewer provenance fails, it returns `ReviewerProvenanceUnverified`. A positive **synthetic test source** still never grants clearance: it returns only `RelationshipStatusUnknown`, `PotentialRelationshipNeedsIndependentReview`, or fail-closed `DisclosureHistoryCorrupt`. No state or method returns "approved to review", clinic authorization or production selection. Existing Sprint08 sensitive-data interlock remains deny-only.

## Threat model and missing contracts

- `test:` center, reviewer, subject, reporter and relationship-evidence refs prove **no real actor identity, evidence provenance or conflict-of-interest fact**. The source is still a test double, not an IdP or independent credential issuer. Missing disclosure means **unknown**, not independence.
- Real reviewer independence depends on a yet-to-be-approved legal/professional policy (including related-party relationships and institutional ties) and independent review/human accountability. A potential conflict must not automatically punish, exclude, reject or approve a real person.
- Non-keyed SHA-256 and PostgreSQL DML append triggers protect synthetic operational consistency, **not against superuser/schema-owner tampering**. There is no external signed audit/retention, privacy consent, encryption guarantee or production-ready legal data handling. Negative rejections are not yet fully independently audited.
- Real login, issuer, professional medical licenses, valid Taranom contracts, protected center/patient data, clinical access, API/BFF, reviewer UI, payment, insurer or wallet integration remain absent and blocked.

## Acceptance and governance

New isolated genuine PostgreSQL tests cover absent disclosures, potential relationship, multiple notices and unique versions, historical replay/conflict, stale/concurrent writes, exact scoped source note, invalid real/synthetic refs, failed audit rollback, append-only DML protection, corrupt version fail-closed and provenance+disclosure composition. Keep all previous 106 reviewer/evidence tests and twelve independent CI jobs, including Chromium Member and Center UI suites.

**No automatic conflict resolution or human decision; PR Draft/Open.** Independent Code Review, security/privacy/legal review, Stage/QA/Release/Production and any Production promotion remain blocked without explicit authorization.
