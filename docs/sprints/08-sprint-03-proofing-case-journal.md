# TRN-SP-03 — Deterministic in-memory proofing-case review history

Date: 2026-10-10. Scope: internal .NET domain-code iteration in Draft PR #9 only. Product owner has authorized coding but **real proofing/authentication integration remains blocked by external evidence** (Issue #8); independent Code Review remains open (Issue #6 and PR #9).

## What this implements

- A pure immutable **in-memory** proofing case journal for the *already accepted* TRN-BIZ-018…023 scenarios: outage/inconclusive → pending; trustworthy mismatch prior to activation → correction/reverification; identity correction → invalidation of previous proofing.
- Versioned case and identity revisions, exact request-id replay vs changed-payload conflicts, stale-command rejection, and append-only case transition history. No personal identifiers, raw provider response, national ID, medical record, payments or wallet.
- Correction is only modeled after an existing mismatch outcome and an independent **caller-supplied** current activation-status check. A past mismatch never authorizes edits to an already-active member.
- Retained proofing review history is **not** a persistence, retention, privacy or actual authorization contract. In particular, in-memory revision checks cannot replace atomic compare-and-set in a future transactional store, and caller-supplied values must never be trusted without authenticated .NET owner validation.
- A positive observation only results in `ValidatedProofingEvidenceOnly`, not membership activation, valid browser session, 90-day wait start, insurer determination or member credit.
- No retry times/frequencies, IdP/KYC vendor, transport, legal processing purpose, API path, official status taxonomy or data retention terms were invented.

## Verification

Dedicated xUnit tests cover exact/historical replay, conflicting keys, simultaneous expected-revision claims, identity correction invalidation and inability to correct when current activation evidence is missing or indicates an active member. The exact PR head must also pass inherited Sprint 01 PostgreSQL synthetic smoke and Sprint 02 production-bundle Chromium checks; their successes are not real IdP proof.

Remaining admission gates: real identity source and license/contract, lawful subject/member/tenant link, trusted session/revocation, actual authoritative owner data and independent Code Review. No Merge/Stage/QA/Release/Production without those real gates.
