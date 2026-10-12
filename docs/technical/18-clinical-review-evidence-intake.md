# TRN-TECH-018 — Center and clinician evidence intake for independent review

**Date:** 2026-10-10. **Status:** internal synthetic-only contract implementation; no registration, authorization, professional licence validation or Production integration approved.
**Source:** TRN-BIZ-053/054 (center may manage its own physician affiliations; a chain hospital is one center with departments); TRN-BIZ-009/032/043/044 (independent qualified providers and genuine Taranom contract required, clinical quality under authorized supervision); [Business Draft PR #10](https://github.com/mahdimarzooghi4-debug/taranom/pull/10).

## What is new

Isolated `Taranom.ClinicalEvidenceIntake` pure .NET bounded context captures **only unverified, synthetic references to evidence claims** for separate eventual review, with an immutable history per scoped subject and monotonically increasing revision:

| Subject | Claim category | Created output |
|---|---|---|
| Center representative | Center manager authority claim | independent ReviewWorkItem only |
| Physician affiliated to a center | Professional qualification claim | independent ReviewWorkItem only |
| Physician affiliated to a center | Taranom service-provider contract claim | independent ReviewWorkItem only |

There are **no real identifiers or uploaded documents**; every reference must be a strict `test:` marker. Neither a center/doctor account, legal delegation, professional licence, verifiable contract, consent, payment route nor activity permission is created. The status is always **requires independent verification**. Do not interpret `ReviewWorkItemCreated` as an approved review outcome.

`EvidenceIntakeSnapshot.ForSyntheticSubject` binds one center, subject and subject kind. A submission pins expected revision, request ID, claimant ref and opaque synthetic reference. The reducer rejects subject-kind/evidence-kind mismatch, cross-center/subject attempts, stale revisions and malformed/reallooking IDs; exact historical replay is idempotent even after newer submissions, and a changed payload with the same request ID conflicts. Accepted submissions produce an append-only review work item while retaining prior evidence refs (no silent replacement).

No automatic review/approval, status `VERIFIED`, medical licence classification, accept/deny criteria, threshold, provider activation, outbox event, external API, front-end write path or financial settlement exists. Review authority, actual source agency and lifecycle policy are deliberately undecided. No clinician free text / national IDs / actual document content is stored in this foundation.

## Source trust and segregation

- The evidence is always a **claim by an untrusted submitter**, never attested by a real issuer. Synthetic `SubmittingActorRef` must never be considered a validated center manager. A real doctor may not be matched to a `test:` string.
- The result is an internal **review-intake work item**, not a human decision, permission, contract, data access or business eligibility. The prior Sprint 08 interlock must remain closed.
- An actual trusted center organization owner, authorization/identity provider, official physician qualification/legal source, professional responsibility contract, Taranom provider contract registry, audit and consent are still required before any real clinician onboarding or independent review UI is enabled.
- This is a pure in-memory reducer to exercise submission semantics. Version checks are **not** multi-replica safe until an audited transactional persistence and authorized principal are implemented. Previous isolated PostgreSQL center journal persists affiliations only; **do not** conflate it with this new review-intake history.

## Acceptance / gates

The new xUnit matrix covers valid work items, compatibility of representative/physician claims, read-only histories, replay, mismatched actor/center/subject, malformed/real evidence refs, outdated versions, no certified status and absence of spontaneous activation. New eleventh CI job, plus inherited ten independent .NET, PostgreSQL and Chromium jobs on exact SHA.

Independent Code Review pending; do not mark Draft PR Ready, merge, deploy Stage/QA/Production or imply real provider verification.
