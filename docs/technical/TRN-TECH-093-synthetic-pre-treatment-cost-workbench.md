# TRN-TECH-093 — Local five-source pre-treatment cost workbench

**Status: Sprint 84 test-only technical contract; no real member endpoint or release authorization.**
Derived from accepted TRN-BIZ-051 / TRN-BIZ-052 and the established internal `SyntheticCostPreviewBuilder`.

## Boundary and minimal authority

The existing `Taranom.OperationsStatus` Kestrel server is strictly bound to loopback port 4318 with exact Host and forward-header denial. A **single, opt-in, POST-only** test route `/internal/ops/v1/synthetic-cost-workbench` is added to that already local-only process. It returns 404 unless the exact environment value `TARANOM_SYNTHETIC_COST_WORKBENCH=CI_EPHEMERAL_ONLY` is set. Requests must originate from the exact same local origin; no CORS or credentials. Request length <=8192 bytes; exact JSON keys (no duplicates/extras), JSON depth limit, exactly five lanes, and only `test:` source/member refs. No new public `/api` route, login/session, persistence, insurance, pricing, credit or payment adapter.

A purpose-specific public **pure test bridge** `SyntheticCostWorkbench.Inspect` converts string/nullable inputs to the internal canonical domain types and calls `SyntheticCostPreviewBuilder.Build`; it does **not** make the entire governance assembly internal surface accessible to OperationsStatus. No `InternalsVisibleTo` addition. Only the domain builder determines validity, five independent source states, canonical order and SHA-256 fingerprint. No arithmetic, aggregation, entitlement calculation, debit or final invoice.

## Request/response

`{memberRef:"test:member84",previewId:"<UUID D>",previewRevision:1,lines:[...five...]}` with each line containing exactly `component,state,amount,currency,sourceRef,evidenceRef,sourceRevision`. Components are `ProviderService,PrimaryInsurance,SupplementalInsurance,TreatmentCredit,MemberRemainder` once each. State is `Missing,ProvisionalClaim,ConflictingClaims`. For `Missing`, optional strings must be null and revision 0; for `ProvisionalClaim`, amount must be a valid nonnegative decimal string, currency an uppercase three-letter ISO-shaped claim, and source/evidence/revision present; for `ConflictingClaims`, amount and currency must be null but source/evidence/revision are required. Evidence identifiers are synthetic UUIDs, never provenance proof.

Success returns `MissingOrIncomplete`, `Conflicted`, or `AllComponentsProvisionalOnly` and exactly five *unverified* line states, canonical fingerprint, and **all** rights flags false. Invalid/malformed/unknown/untrusted claims fail closed with 400 or 404; never show a fabricated zero or disputed amount. No computed total, final member remainder, benefit approval or available wallet credit.

## UI / version semantics

Only the local CI build with `VITE_TARANOM_LOCAL_COST_WORKBENCH=1` and exact loopback origin/port 4318 renders the form within existing `/member/costs`. The default/disconnected production-style member build stays untouched. Form starts with all five `Missing`; tester explicitly edits test refs, UUID evidence and claimed values. **No implicit POST on page view or edit.** Explicit "evaluate" sends one same-origin POST, with no credentials or persistence. Any input or revision change clears previous result immediately. There is no actual choice/acknowledgement in the workbench: historical choice is never carried forward, consent and legal financial commitment are always false. Existing disabled real-member consent controls and independent urgent-care warning remain visible.

The replay/continuity and legal authorization designs outside this synthetic test are not activated. No patient/private data, actor identity, insurer, source legitimacy or emergency-treatment gating is inferred. All 17 launch blockers remain `BLOCKED_UNVERIFIED`, `DENIED_BY_DEFAULT`, overall completion `UNBASELINED`. The Sprint PR remains Draft/Open until independently reviewed and explicitly authorized.

## Evidence gates

The inherited 14 shared full-regression jobs remain intact. The existing real-loopback HTTP + Chromium job additionally exercises accepted missing/provisional/conflicted test evidence, revisions and fingerprints; invalid components, duplicate/extra keys, non-test refs, forged rights, foreign origin, absent Origin, wrong Host, method, oversized bodies and opt-in-off denial. No separate deployment, Stage or release gate is implied.
