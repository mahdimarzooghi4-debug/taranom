# TRN-SP-48 — Insurer dispute source-pinned handoff and billing correction fence

**Date:** 2026-10-11. **Branch:** `sprint-48-insurance-dispute-source-handoff-lineage`. **Base:** Sprint47 PR #54, stacked Draft/Open/Unmerged.

**Parent:** TRN-BIZ-035/042/048/049/050 → [TRN-TECH-057](../technical/57-insurer-dispute-source-handoff.md) → backlog TRN-EP-07/09/10/13 → Sprint48 → code/tests → 12-job exact SHA CI → initial Code Review COMMENT.

**Feature:** combine existing (not replaced) `SyntheticSupportIntake` insurer-specific member dispute history and `SyntheticTreatmentBillingAssembler` disputed legal-source lane into one immutable **unverified paper handoff**. Bind current disputed primary/supplemental insurer source UUID/revision, insurer/direct-or-intermediary route, exact member/case/packet/treatment; reject source drift on later paper notes. Forward request ≠ receipt ≠ legal decision; no insurer connection, payee or wallet change. Keep medical-service complaint independent. Harden negative C# replay and source tampering tests; clarify three stages in disabled Member RTL support UX with 4 real Chromium tests, preserve all CI incl. real isolated PostgreSQL.

**Gate:** signed insurer contract and official claim/response, member consent/data, legally authorized correction and billing reconciliation, payment service, clinical/legal/privacy review and real national infrastructure OPEN. No Stage/Ready/Merge/QA Gate/Release Approval/Production. Nationwide official launch DENIED_BY_DEFAULT.
