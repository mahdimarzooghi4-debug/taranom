# TRN-SP-191 — DRAFT / Member cancelable read-only source with late-response isolation

Parent: Draft/Open Sprint 190 [PR #204](https://github.com/mahdimarzooghi4-debug/taranom/pull/204) HEAD `1431fdb7d49df0504c6f5cfcefa88d1d44780b05`.
Technical: [TRN-TECH-200](../technical/TRN-TECH-200-member-care-read-cancel-isolation.md).

Functional behavior: Explicit Member Cancel for ONLY three immutable source GET workflows (case read, return-claim verification, pinned event history). Strict generation-fenced late successes/failures cannot resurrect an old case, return receipt, controls or counts after cancellation/another selected case; cancellation unlocks selection without pretending to abort a server transaction. Command POST and uncertain-UUID recovery cannot be canceled. Preserve all existing source attestation and national launch denials. Five real Chromium/Kestrel/isolated PostgreSQL adversarial timing tests, exact-head full 14-job CI, technical COMMENTED review only. Draft/Open/Unmerged, no Ready/Merge/Stage/QA Gate/Release/Production.
