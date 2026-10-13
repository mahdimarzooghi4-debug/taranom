# Sprint 104 — Test-only identity correction and renewed-proofing source journal

**Date:** 2026-10-13 | [TRN-TECH-113](../technical/TRN-TECH-113-synthetic-proofing-correction-journey.md) | **Business:** accepted TRN-BIZ-018…023 | **Epics:** TRN-EP-01/03/12/13.

**Parent:** Sprint103 Draft/Open PR #114 exact `8b1d22478886dcee484bc4e40d9a447e688d9d04`, Push #38392749170 and Mainline #38392806766 both 14/14 SUCCESS, preliminary technical COMMENT review only. No independent human approval.

**Complete bounded workflow:** reuse preexisting append-only `PgProofingCaseJournal` with source-replayed reducer and transactionally paired audit. Initialize only ephemeral schema in existing isolated Kestrel; add exact test-ref JSON POST and test-only GET, exposing real historical case/identity revisions and *only* unverified source claims. Add Persian RTL stepwise browser flow for claimant mismatch, declared correction, and required fresh observation; stale/reused/cross-case inputs fail closed. Include manual exact-UUID retry after uncertain ACK, no implicit writes or personal information. Python HTTP and real Chromium acceptance added, no other test/control removed.

**Safety:** `Confirmed` flags are test-supplied claims, **not verified evidence**. No real applicant, editable national ID, licensed IdP, member login, membership activation, 90-day waiting period, wallet, insurance, clinical record or financial action. Real member BFF TRN-TECH-012 NOT CODE-AUTHORIZED. Full 14-job Push+Mainline CI and initial COMMENT review required. Draft/Open/Unmerged, 17 national BLOCKED_UNVERIFIED and launch DENIED_BY_DEFAULT; no Stage, QA Gate, Ready/Merge/Release/Production.
