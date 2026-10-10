# TRN-TECH-039 — Separated synthetic service-complaint / insurance-dispute intake

**Date:** 2026-10-10. Based on accepted **TRN-BIZ-045** (unified Taranom member-facing complaint intake/follow-up, legally independent medical review and fault determination) and **TRN-BIZ-049** (unified insurance claim-dispute communication with insurer retaining legal entitlement/coverage determination). Stacked on Draft Sprint29 PR #36. This is a **nondeployable test-only foundation**, not a complaint authority, insurance adapter, clinical incident handler or real member service.

## Product gap and precise boundaries

The existing Member Web has only one disconnected `/member/support` section with generic text, missing explicit domain separation between service quality/medical incidents and insurance eligibility/coverage disputes. Technical work must never conflate an insurer's decision with a physician's professional accountability or treat either as a Taranom adjudication.

## Domain: synthetic-only `SyntheticSupportIntake`

Create a pure .NET, internal, revisioned and append-only reducer with exactly two independently typed **unverified** topics (`MedicalServiceComplaint`, `InsuranceCoverageDispute`). Two permitted **technical evidence actions** are `SubmitUnverified` and `RecordUnverifiedFollowUp`; there is no route to approve, reject, adjudicate, determine blame, decide coverage, grant compensation, pay, reimburse or close a legal case.

The submission accepts a `test:` case, `test:` member, exact topic, `test:` opaque evidence, `test:` claimed submitter marker, a unique request UUID and expected revision. Every later follow-up requires the same immutable case, member, topic, evidence and submitter marker, the exact original submission UUID, a `test:` claimed support actor and `test:` opaque follow-up note. Claimed actor markers are **not authenticated identities** or evidence that a doctor or insurer was contacted. No free-text medical content, patient identifier, policy ID, health record, prices or wallet amount may be stored.

Exact historical command replay is idempotent even after later notes; request-key reuse with changed payload is rejected; stale revision, missing original, invalid identity, unknown topic/action, second new submission and cross-topic or cross-member note substitution fail closed. The immutable in-memory read projection returns only source topic, claimed case/member, unverified note count and revision; it conveys **no real lifecycle state** (assigned, underway, resolved, approved or time-to-response). There is no real storage/database/API or denial audit; no server verification of `test:` actor claims.

## Member Web deliverable

Maintain `/member/support` as one coherent member-facing workspace, while showing TWO clearly differentiated informational cards: **medical service complaint/incident** and **insurance coverage/share dispute**. Each names its *own responsible legal authority*, restricts sharing of health details, and clarifies that insurer disputes alone NEVER debit/block/change member wallet. No fabricated ticket IDs, SLA, deadline, issuer, reviewer, provider or resolution. Each submission CTA remains explicitly **DISABLED**, no real member account, private API, session, health data or financial mutation. Preserve existing `connect-src 'none'` CSP and noindex/no-referrer. Show emergency notice: urgent care must not wait for complaint/insurance administrative process.

## Evidence and safety gates

New xUnit tests: independent topics, append-only two-note projection, exact replay after later notes, conflicting replay, stale revision, missing original, cross-topic/member/case/evidence/submitter/request substitution, invalid real and synthetic refs, duplicate intake, forbidden decision vocabulary. Four real built-Vite Chromium tests validate both domain-specific informational paths, disabled submission/no API, no fabricated ticket or SLA, emergency information and browser history. Inherit twelve exact-head CI jobs including member and center browser, real PostgreSQL, identity and clinical data **DENY-ONLY**. No actual patient record, insurance policy, fake administrator, legal review or compensation decision.

## Unresolved operational inputs

Official organization and delegation, lawful member authentication, real provider/licensed clinical reviewer and independent regulator/insurance authority, full contract and data privacy rights, external sources, verified disputes/medical incident escalation, legally controlled notification and retention, professional conflict rules, financial settlement/insurance adjustment governance, actual safe emergency workflow and independent legal/medical/security review remain blocked. **No external connection or authority** was invented; there is no Stage, QA Gate, Release or Production admission.

PR remains Draft/Open/Unmerged. No Ready for Review, Merge, Stage, QA Gate, Release or Production without explicit instruction.
