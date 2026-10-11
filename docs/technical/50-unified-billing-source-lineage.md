# TRN-TECH-050 — Synthetic unified treatment-billing source lineage

**Status:** internal technical contract for Sprint 41, 2026-10-11; **nonoperational and test-only**. Authority: accepted TRN-BIZ-048, TRN-BIZ-049, TRN-BIZ-050, TRN-BIZ-051/052, TRN-BIZ-008–011 and TRN-BIZ-029/035. Backlog: TRN-EP-07/09 and member-facing TRN-EP-13. Stacked after Sprint 40, without merging.

## Product boundary

An **after-service bill evidence view** is different from a **before-service nonbinding cost estimate** already covered in Sprint 28. TRN-BIZ-050 authorizes a unified member presentation of five independent *categories*, not issuance of official invoices or the calculation/approval of actual shares. Insurance coverage belongs to authorized insurers; treatment documents belong to their lawful issuer; noncashable treatment-credit responsibility requires valid financial authority; the member pays any legally established remainder directly to the contracted provider only according to validated real records. Source, provider/payee contract, member/payer privacy, financial correction and regulator remain unresolved.

## Source contracts

`SyntheticTreatmentBillingAssembler` is a **pure internal .NET read assembler** (no in-memory journal, DB, API, service, HTTP, financial commands or actual identities). A scoped packet requires test-only member/treatment refs, a UUID packet identity, positive view revision, and caller-supplied synthetic evidence across **five typed lanes**:

1. Provider service cost, claimed origin provider's lawful document issuer.
2. Primary insurer contribution, claimed origin primary insurer.
3. Supplemental insurer contribution, claimed origin supplemental insurer.
4. Noncashable personal treatment credit, claimed origin licensed financial/credit custodian.
5. Claimed member remainder, expected origin provider document issuer (not legal validation of member debt).

Each event pins exact member/treatment/packet, lane and claimed issuer, `test:` source reference, source revision, unique evidence UUID, lane-local revision and a link to the previous evidence UUID if corrected. The first revision must have no predecessor; subsequent versions must have strictly contiguous revisions and point to the exact preceding event. Multiple lanes cannot reuse evidence IDs; supplied order does not change the canonical SHA-256 fingerprint. The issuer mapping is a **test vocabulary, not an attested actor or actual official invoice**.

Evidence changes are `UnverifiedClaim` (optional **zero is a numeric value only if explicitly supplied**; nonnegative decimal and upper-case 3-letter currency required), `Disputed` and `Withdrawn` (both **must have null amount/currency**). A dispute or withdrawal masks the older number. Future corrected claims may be supplied only with an exact link and remain **unverified**. The read includes five fixed lanes and distinguishes `MissingOrWithdrawn`, `Disputed`, `IncompatibleCurrencies`, and `UnverifiedClaimsOnly`; malformed metadata produces `Invalid` with **no leaked data**. These are internal display/testing states, not approved insurer, invoice or settlement states. Source history SHA is not an issuer signature or valid legal document.

**Prohibited:** summing shares, deducing a missing insurer share as zero, deriving member debt, selecting currency rates, confirming insurance eligibility, calculating actual wallet balance or spendability, allocating/locking/charging credit, paying providers or members, issuing final invoices, resolving disputes, recording actual patient/medical/financial data or reusing these claims for AI training. An employer or sponsoring payer has no entitlement to the member's detailed bill.

## UI + acceptance

The existing member route `/member/billing` becomes a five-lane, Persian RTL **read-only post-service document-source overview**, each showing `اطلاعات موجود نیست`, expected lawful issuer and absence of verified documentation. Explicitly distinguish from `/member/costs` (nonbinding pre-service estimate), explain correction/dispute through a qualified source, privacy and prohibition on auto-correcting a wallet. All download, dispute, payment and settlement controls remain disabled; no receipt number, provider, insurer, amount, official status, medical facts or actual source. Built Chromium must prove five unknowns, disconnected behavior, history/focus and zero private network/storage. .NET xUnit must prove scope isolation, lane-to-authority map, exact chain and correction, duplicate collisions, amount and currency validation, dispute/withdrawal hiding, canonical fingerprint and absence of payment vocabulary.

## Gates that remain closed

No active member session/BFF, real lawful provider or insurer API, provider-issued invoice or regulated credit custodian, reconciled ledger/corrective legal record, legitimate financial settlement, legal treatment/insurance consent, independent financial/privacy/security/clinical review or national rollout evidence. No Stage/QA Gate/Ready/Merge/Release/Production. Release remains **DENIED_BY_DEFAULT**.
