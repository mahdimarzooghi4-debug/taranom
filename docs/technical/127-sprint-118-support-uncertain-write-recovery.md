# TRN-TECH-127 — Sprint 118 internal support uncertain-write recovery

**Status:** TEST-ONLY, isolated localhost, PostgreSQL / React / ASP.NET Core.
**Authority:** reuse existing TRN-TECH-075 command protocol, version CAS, request-ID idempotency and append-only audit journal. No new API, external provider, legal authority or Business rule.

## Failure and recovery boundary
- Every attempt freezes the **entire original JSON body**, case reference, expected revision and UUID v4 before transport. Uncertain outcomes (network drop, non-200, unverified response) must not mint a new UUID, update fields or claim success.
- A retry posts the **identical bytes** to the **identical case route**. Server must recognize the original exact replay (including after intervening newer history) or reject stale/conflicting writes. `RecordedUnverified` must return exactly the next version; `Replayed` may return a later current revision but must match the original command's case, member, topic, evidence, submitter and event at the original version.
- A human-portable, **unsigned and untrusted** `TRN-TECH-127` receipt carries only test refs plus the exact original POST bytes. Its strict parser rejects changed keys, duplicates, malformed UUIDs/revisions, non-test refs and cross-case imports. Import never automatically submits; explicit human retry still goes through the .NET bounded command and PostgreSQL checks. No browser storage is used.
- While pending, local case navigation and edits are disabled. A conflicting or unverified result remains uncertain; no fake success and no fresh request creation. Browser reload requires the operator to have kept the portable receipt; absence of a receipt does **not** prove the original operation failed.

## Acceptance
Real browser + .NET + PostgreSQL tests exercise: accepted DB commit followed by lost response and exact replay; network loss followed by portable receipt import and replay; invalid receipt with no write; competing stale write denied without duplicate. Full 14-job CI, reviewed diff, Draft PR and independent human approval requirements remain.

## Deliberate exclusions
No Production identity or session; no legal complaint, insurer contact, clinical authorization, financial decision, data from outside Taranom or official member activation. The test-only `CI_EPHEMERAL_ONLY` surface does not become a member application. No Stage, QA Gate, Release Approval, Merge or Production. National release stays `DENIED_BY_DEFAULT`.
