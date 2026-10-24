# TRN-SP-167 — DRAFT / guided nonclinical member coordination

Base: Sprint 166 PR #180 Draft/Open, exact SHA `fbb73948c02627a0fa17dfa0de71191020c0e37f`.
Technical contract: [TRN-TECH-176](../technical/TRN-TECH-176-guided-nonclinical-member-coordination.md).

Deliver: a source-backed, human-click-only guided experience over the **existing** nonclinical, unverified care-coordination case journal, with separate display of the two kinds of claims, no inferred contact/medical outcome, and read-only recovery of unknown ACK. No new clinical decisions, authorization, provider integrations, persistence schema or API route. QA: real local PG+HTTP+Chromium 3 tests, original suites, 14/14 canonical CI, exact-head technical COMMENT review. No independent human approval/merge/Stage/Production; national release DENIED_BY_DEFAULT.
