# TRN-TECH-053 — Synthetic internal-AI dataset consent and partition inspection

**Recorded:** 2026-10-11. **Scope:** internally testable, fail-closed, **nonoperational evidence inspection only**. Stacked on Sprint43 PR #50. Business: approved TRN-BIZ-033 (nondiagnostic screening), -035/-036 (member-controlled data and independent consent), -037 (internal AI / ban external services), -038 (joint clinical/technical committee and independent final human activation), -039 (governed automatic *potential* training, never automatic Production), -030/-040 (comprehensive nationwide first official release). Backlog: TRN-EP-02/11/13.

## Existing boundaries reused, not duplicated

- `SyntheticSecondaryUseEvidenceGate` is the existing Sprint32 independent health-data/research consent **prerequisite assessment**. Confirmed flags are untrusted assertions; even `EvidencePresentOnly` is **not** permission to consume member data.
- `SyntheticAiReleaseEvidenceTrail` is the existing Sprint33 distinct Candidate → claimed joint committee packet → claimed final human packet record **without** a Production router or verified authorization. It is NOT changed and does not accept output from this Sprint as candidate approval.
- The external-AI no-endpoint policy, national launch blockers, no private APIs and real PostgreSQL synthetic-only tests stay intact.

## Sprint44 intake gate

New `SyntheticAiDatasetBoundary.Inspect`, internal pure .NET, accepts a **claimed** dataset version identifier and SHA-256-shaped manifest digest, explicit claimed training-policy ref and revision, exact tenant/research-purpose/data-category, and claimed training-policy, independent-evaluation and human data-governance flags. Each source is **only an opaque `test:` reference** + version + SHA-256-shaped digest, partition flag (Training / IndependentEvaluation), a complete existing `SyntheticSecondaryUseEvidence` vector for its own member, and four separate source-rights/integrity/quality/partition lineage flags. No medical record contents, identifiable member data, cash, model weights, trainer, API, actual Dataset entity, consent grant or training command.

**Fail-closed invariants:**
1. Every synthetic source must match exact manifest tenant/purpose/data-category. A care consent, expired/rejected consent, insufficient scope, missing lawful basis or revoked/unknown withdrawal protection cannot be inferred as independent research consent. Call the existing gate for each entry; no alternate consent rules.
2. Every claimed source must pass schema and version checks; any repeated source reference in either training or evaluation fails, **including when a different source revision is supplied**. Both partitions must contain structural references. This is not a sufficient sample-size policy and does **not** claim clinical/ML evaluation independence across the same member.
3. Training policy must be **explicitly claimed configured**, independent evaluation governance and human data review separately claimed; **no trigger frequency, sample-size, numeric quality threshold, algorithm, model selection or approval authority is invented**.
4. Any rejected current consent/lawful basis or source/policy flag is denied. Any absent/unknown flag is unavailable; every claim of Confirmed results **only** in `ClaimedPreconditionsPresentOnly`, never `EligibleDataset`, `TrainingStarted`, `CandidateCreated` or `ProductionActivated`.
5. A canonical SHA-256 of the complete *caller-supplied metadata*, order invariant across distinct source refs, is returned only for structurally present claims; it is an **unverified regression fingerprint**, not a signed data manifest or proof of rights. Failure returns null fingerprint and zero counts. The typed result counts source labels, not people or real medical samples.

## Member UX and acceptance

The existing Persian RTL member `/member/consents` will explain the **separate health-data research/AI lifecycle**: independent purpose-limited informed consent, legally rights-cleared/protected health source, independently separated training/evaluation, and separately reviewed Candidate → joint medical/technical committee → final accountable human activation. Every real consent/source/dataset/model/review state remains **unknown** and disabled; no opt-in by default, no claim of training, no external AI, no data requests/storage. Four built-browser security and navigation regressions complement new negative xUnit source/scope/consent/partition/policy/fingerprint cases.

## Deployment and financial/medical constraints

No real identity or health source, authenticated consent provenance, withdrawal/retention/deletion semantics, lawful datasets, approved training schedule/configuration, vetted weights/license, evaluators/quality thresholds, medical committee, final authority, monitoring/recovery or hosting yet exists. No real training or candidate creation is authorized. No source records must flow to any AI component; no external AI API/SDK/endpoint. First national official release remains **DENIED_BY_DEFAULT**. No Ready/Merge/Stage/QA Gate/Release Approval/Production without explicit owner instruction.
