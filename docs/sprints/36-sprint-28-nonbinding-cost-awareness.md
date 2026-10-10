# TRN-SP-28 — Nonbinding pre-treatment cost awareness / source visibility

**Date:** 2026-10-10. **Base:** `sprint-27-late-evidence-work-item` Draft PR #34. TRN-BIZ-050/051/052 → TRN-TECH-037 → TRN-EP-09 and TRN-EP-13 → Sprint28 Code → Tests → exact-head CI → preliminary Code Review.

- **BL-280** — New pure .NET `SyntheticCostPreviewBuilder` enforces five unique source-typed components and explicitly distinguishes missing from supplied literal zero, provisional claim and conflict. No calculation, guarantee, insurer approval or source trust. All IDs are `test:` and no real data is permitted.
- **BL-281** — Bind an independent `CostAwarenessRecorder` in-memory test reducer to exact preview fingerprint/version/member with idempotent replay, stale/version and cross-member fail-close; a recorded test choice has **no financial, medical or legal effect**.
- **BL-282** — Deliver an actual Member Web `/member/costs` presentation section exposing 5 **unknown/unavailable** components; do not fake amounts, show unverified rates, insert a checkout, or enable signed consent. The choice control remains disabled.
- **BL-283** — Add source/cross-currency/zero/unknown/adversarial xUnit and 3 real built Chromium cases, preserving all inherited 12-job CI, DENY-ONLY clinical data gate and `connect-src 'none'`.
- **BL-284** — Preliminary source/security review COMMENT, exact SHA Push+PR CI, account for failures directly. Independent finance/legal/medical/security reviews remain mandatory before any real data integration or Stage/Release.

No actual underwriting/insurance/authoritative quotes, banks, real wallet movement, medical urgency gate, network contract or member authenticated BFF is introduced. This is contract + transparent disconnected UX only. No Merge/Stage/Production.
