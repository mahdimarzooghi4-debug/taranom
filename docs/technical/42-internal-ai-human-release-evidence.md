# TRN-TECH-042 — Internal AI candidate versus joint committee and separate human release evidence

**Date:** 2026-10-10. Base Sprint32 PR #39 Draft/Open. Business TRN-BIZ-033/036/037/038/039: only internal AI under Taranom control, nondiagnostic screening only, real eligible data requires separate research/AI consent + protection + legal rights, governed automated learning may create Candidates, but joint medical-technical review and an **independent explicit human release approval** are both required before any real Production activation.

## Actual implementation boundary — TEST ONLY

The project has **no authorized Training Engine, real dataset, model registry, trial evidence, verified committee, or production model**. The committee's composition, professional credentials, vote/quorum, technical/clinical evaluation thresholds, emergency stop/rollback rights and the real final accountable officer have NOT been chosen. These MUST NOT be guessed, and **no training/AI runtime, network endpoint/token or production release router is added**.

Instead, new internal pure C# `SyntheticAiReleaseEvidenceTrail` records ONLY three sequential types of unverified **`test:`-scoped paper evidence**. The three stage names intentionally do not say approved or authorized:

1. `RecordUnverifiedCandidate`: one synthetic candidate ref, one synthetic dataset-version ref, a caller-claimed uppercase SHA-256-shaped digest, request UUID and expected revision. This is NOT an actual trained artifact, authenticated dataset eligibility or consent proof.
2. `RecordUnverifiedJointCommitteePacket`: immutable binding to **exact same candidate, dataset version, digest and original candidate request UUID**; a claimed medical-role actor and **distinct claimed technical-role actor**, plus an opaque synthetic joint packet ref. Neither actor is verified; distinct strings DO NOT imply an actual committee, quorum, professional licence or successful safety evaluation.
3. `RecordUnverifiedFinalHumanPacket`: possible only after candidate and joint packet; bound to same exact candidate/dataset/artifact and original UUID, with a third claimed authority actor distinct from both claimed committee actors and an unverified final-human packet. This is NOT a legally valid decision or a Production promotion command.

Input validated strictly: bounded `test:` refs (never actual identity/health data), exact 64-digit uppercase hex digest shape, unique nonempty request UUID and nonnegative expected revision. Exact historical replay returns the latest state; identical commands do not advance revision, changed payload with same request UUID returns conflict, stale version is rejected; no duplicate Candidate/Committee/Final stage, no final packet before joint packet, no cross-model/dataset/digest/original UUID substitution, no same-person-claimed actor for medical vs technical or final vs either committee role, no legacy real owner field or medical data.

`SyntheticAiReleaseEvidenceProjection` returns ONLY counts/booleans that **unverified test packets were recorded** and a synthetic revision. It MUST NEVER be used as admission to a real model deployment, any data-use permission, training trigger, assessment of model safety or permission for clinical practice. The existing `SyntheticSecondaryUseEvidenceGate` Sprint32 is also a test-only prerequisite structure and even its `EvidencePresentOnly` cannot authorize a dataset. This sprint implements no real source-to-candidate path and does not implicitly convert the hypothetical three recorded packets into human authorization.

## Test / CI admission

Full deterministic xUnit test matrix covering valid stage order with no activation, refusal of final before committee, duplicate stages, exact replay after later evidence, changed-request conflicts, per-field cross-candidate/dataset/artifact/origin scope substitution, separate claimed medical/technical/final actor markers, missing/unsupported source and revision handling, and lexical nonexistence of Production/training/authorization commands. Inherits **12 independent jobs**: MemberGovernance xUnit, real PostgreSQL identity/proofing/clinical evidence, both Chromium shells and medical protected-data DENY-ONLY.

No alteration to user-facing clinical app, synthetic consent PostgreSQL, medical access rights or financial paths. No actual committee review. Independent legal/medical/privacy/financial/security specialists have not approved. PR remains Draft/Open/Unmerged; no Ready for Review, Merge, Stage, QA Gate, Release or Production without explicit user instruction.
