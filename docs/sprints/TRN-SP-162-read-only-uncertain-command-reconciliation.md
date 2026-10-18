# TRN-SP-162 — DRAFT / safe read-only uncertain-command reconciliation

- Base: Sprint 161 Draft/Open PR #175, exact head `bea82af9b02230078147d203cdcc1c474f807570`.
- Technical draft: [TRN-TECH-171](../technical/TRN-TECH-171-read-only-uncertain-command-reconciliation.md).
- Scope: implement deliberate one-click GET-only journal reconciliation after lost/forged ACK in three existing **synthetic, CI-ephemeral** member vertical slices. Do not send a new command to answer whether the original was recorded. Keep original manual same-UUID replay as fallback when a read cannot establish the exact persisted event.
- Evidence: test real committed events, zero-retry POST count, forged response rejection, lock preservation, manual replay, old negative scenarios, full 14 CI jobs.
- Forbidden: real identities/clinical care/finance, new policy, external providers, evidence approval, model runtime promotion, Production .NET/Python bridge, Stage, QA Gate, Release, Merge, Ready for Review or Production. PR must remain Draft/Open/Unmerged and national release `DENIED_BY_DEFAULT`.
