# TRN-TECH-035 — Closed historical synthetic membership coverage window observation

**Date:** 2026-10-10. Base: Draft Sprint25 PR #32. Business source: accepted TRN-BIZ-012 (prior treatment credit retained after nonrenewal; no new credit during lapse), TRN-BIZ-013 (new accrual only from reactivation), TRN-BIZ-014 (late-recorded benefit from prior valid membership requires human-approved review of supporting evidence, no automatic credit), TRN-BIZ-015/016 (initial wait exactly 90 full days from official activation, not payment or registration). Only those accepted distinctions are modeled; no formula, business threshold, source credential or effective financial entitlement is inferred.

## Problem

The existing pure `MembershipWaitingPeriod` reducer handles ONLY the initial 90-day clock. It does not distinguish a **retrospective attribution period** belonging wholly to recorded membership from a period fully inside an **explicit nonrenewal gap**, or a claim spanning both. This creates a risk of future attribution logic accidentally crediting a gap merely because a benefit was registered late.

## Narrow implementation contract

1. New separate, **internal test-only** pure .NET reducer `SyntheticMembershipCoverageWindow`. Its inputs are a requested **synthetic `test:` subject**, an ordered untrusted caller-supplied collection of `SyntheticMembershipBoundary` with integer revision, unique event UUID, exact subject, kind (`ActivationRecorded`, `NonRenewalRecorded`, `ReactivationRecorded`) and effective instant, plus query `[periodStartInclusive, periodEndExclusive)` and an explicit caller-supplied observation instant.
2. No marker is an authoritative official activation or entitlement. Enforce strict ascending UTC-equivalent effective instants, contiguous revisions starting at 1, unique nonempty IDs, one exact member subject, initial activation, and alternating nonrenewal/reactivation. Never sort, deduplicate, reinterpret suspension/cancellation, fill missing events, substitute payment/registration timestamp, or automatically infer a policy.
3. A **positive-sounding temporal observation** (`WithinClosedRecordedMembership`) is returned **only** when the entire queried attribution period falls inside one fully closed observed membership segment `[activation,nonrenewal)` or `[reactivation,nonrenewal)`. `WithinClosedRecordedGap` is returned only inside a closed nonrenewal-to-reactivation segment. Queries spanning both are `CrossesRecordedBoundary`; unclosed latest intervals, absent history and periods outside the observed span return `EvidenceInsufficient`. Malformed sequence returns `InvalidHistory`, invalid input returns `InvalidInput`.
4. This is intentionally a **classification of hypothetical recorded interval structure**, not confirmation that membership genuinely activated, money accrued, source income existed, evidence was approved, a credit is payable, or treatment may be authorized. A true benefit requires separate verified human/financial evidence according to TRN-BIZ-014; no credit journal or wallet interaction is created.
5. Validate retrospective UTC instants, boundary precision (end-exclusive), offsets, initial and reactivation windows, gaps, missing/invalid events, duplicate/missing revisions, scope tampering, reverse/future queries and absent references in xUnit. Preserve all 12 inherited CI jobs and protected-data DENY-ONLY. Do not modify `MembershipWaitingPeriod` or make a new 90-day waiting policy.

## External blockers unchanged

Source of authoritative effective activation/nonrenewal events and their corrections, criteria for identity verification, right/consent to process actual data, approval of late attribution and income proof, exact wallet posting/reversal/idempotency, finance backing, bank/insurer, licensed care providers and reliable data access are still **unselected/unverified**. Synthetic `test:` references are not credentials. This code must not be exposed as a public API, SQL table, production financial rule or clinician permission.

No Ready for Review, Merge, Stage, QA Gate, Release or Production without express instruction and independent security/legal/clinical approval.
