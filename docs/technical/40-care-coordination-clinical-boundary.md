# TRN-TECH-040 — Nonclinical care coordination versus clinical decision isolation

**Date:** 2026-10-10. Based on ACCEPTED **TRN-BIZ-046** (Taranom coordinates member service experience; lawful clinician owns medical diagnosis, treatment and specialist referrals) and **TRN-BIZ-047** (member participation/privacy; Taranom prevention and longitudinal *coordination*, licensed care team owns clinical care). Also preserve **TRN-BIZ-034** (independent immediate non-diagnostic emergency warning with parallel authorized clinical-team referral, only after real emergency safety protocol/integration) and **TRN-BIZ-035** data-use protection. Stacked on Sprint30 PR #37, Draft/Open.

## Why this matters

The Member Web already links to `/member/care`, but only repeats a generic unavailable notice. This can lead to a misleading future assumption that the coordination UI itself can authorize specialist referral or manage long-term clinical care. The new internal vocabulary and actual static UI explicitly separate **contact coordination claims** from **unverified references to clinician-origin documentation**. The work is intentionally self-contained and can be verified without guessing provider, legal or emergency service integrations.

## Backend contract — internal, synthetic, pure .NET

`SyntheticCareCoordinationTrail` in `Taranom.MemberGovernance` is an internal in-memory, **nonoperational** technical reducer. It accepts exclusively `test:` synthetic references and request UUIDs. It has three append-only actions, with **no medical execution commands**:

1. `RequestNonclinicalCoordination`: starts a new unverified trail for a synthetic case/member and an opaque `test:` member-request reference, with claimed actor/document fields absent and no clinical meaning.
2. `RecordUnverifiedCoordinatorContact`: appends a claimed coordinator's `test:` actor and `test:` opaque contact-note reference to the exact original case/member/request and UUID. This is not proof the person is a real coordinator or a provider actually responded.
3. `AttachUnverifiedClinicalSourceReference`: appends a claimed clinical-source actor reference and opaque document-reference, pinned to the original submission. It **never verifies the clinician's identity, document authenticity, licence, patient-consent, clinical order, referral acceptance, treatment or care-plan status**.

All later actions require exact original `CaseRef`, `MemberRef`, `OriginalMemberRequestRef` and `OriginalSubmissionRequestId`, a distinct request UUID and expected revision. Cross-case/cross-member/source swaps, duplicate new starts, stale expected revision, unknown kind, missing origin, invalid/real unscoped references are denied without mutation. Exact historical replay returns current snapshot even after later events; reusing UUID with changed payload is conflicting replay. The projection `SyntheticCareTrailRead` returns **only revision and unverified coordinator-contact and clinical-reference counts**, not a status/diagnosis/provider/referral decision. There is NO API, persisted journal, real emergency-event producer, scheduler, AI, identity/consent, provider dispatch or legitimate clinical data. A claimed reference is an untrusted string, never an accepted order.

## Member Web — accessible, disconnected Persian RTL

The existing `/member/care` page now visibly distinguishes three **responsibility descriptions**, all with `اطلاعات موجود نیست`: (a) nonemergency administrative contact, for which Taranom is responsible for coordination experience; (b) specialist clinical decision and referral, owned exclusively by a lawful clinician/provider; (c) after-service follow-up, where Taranom coordinates experience but licensed clinicians make care decisions. **No appointment, clinical care plan, provider coverage, completed referral, actual member or care record is displayed.** One disabled `درخواست هماهنگی — نیازمند هویت و مجوز واقعی` control; no invented slot, contact number, SLA, referral order or health-content recommendation.

The page says urgent assistance is **not delayed by nonemergency coordination or application response**, but does not pretend to implement automated emergency detection/parallel dispatch under TRN-BIZ-034. `connect-src 'none'`, no fake login, no private API and the existing protected clinical data **DENY-ONLY** remain intact.

## Tests and admission

- Isolated xUnit MemberGovernance tests for start, typed coordinator-contact and clinical-ref lanes, counts without execution rights, historical replay after later notes, changed-key collision, stale sequence, missing original, cross-member/case/ref/UUID swaps, real identifiers, invalid/missing actors, duplicate starts and absence of clinical-order/appointment/financial vocabulary.
- Four real built Vite Chromium regressions: 3 responsibility descriptions and unknowns, disabled request/no claimed visit or decision, independent emergency/privacy note and zero private requests, navigation/Back/Forward.
- Full inherited **12-job exact-head CI**: .NET governance, real PostgreSQL proofing/clinical center/clinical review, Member + Center Chromium, center protected-data DENY-ONLY.
- Preliminary Code Review COMMENT only; no independent legal, medical, security or financial approval. Never mark Ready or merge/deploy without explicit owner instruction.

## External gate — deliberately not implemented

Real verified member session, licensed physician/center, patient consent, medical record / authorized access, service and provider responsibility, clinical referral order/acknowledgement, appointment scheduling, follow-up content, safety detection/alerts, provider dispatch, secure clinical messaging, care-plan modification, quality evidence, lawful data retention and capacity remain explicitly **undecided or unconnected**. This sprint does not imply national service readiness.
