# TRN-TECH-041 — Independent secondary-health-data use evidence readiness (DENY-ONLY)

**Date:** 2026-10-10. **Stacked base:** Sprint31 Draft PR #38. **Accepted business basis:** TRN-BIZ-035 (member controls permitted health record sharing, licensed team need-to-know; employer/subscription sponsor cannot receive patient records through payment) and TRN-BIZ-036 (research and internal AI dataset/training/evaluation secondary use requires independent informed consent, effective protection and lawful permissions simultaneously). Keep TRN-BIZ-037 internal-only AI direction; no external AI API. No real consent provider, patient record, lawfully validated revocation policy, dataset or approval exists.

## Existing baseline / not duplicating it

Sprint01's `SyntheticConsentStore` already models separate synthetic purpose streams `CareCommunicationTest`, `HealthDataShareTest`, `SecondaryAiResearchTest`, with PostgreSQL grant/withdrawal, replay and audit negative tests. Sprint03's `MemberSelfReadBoundary` already establishes that self-service member read prerequisites are caller-claimed preconditions, never actual authorization. **Do not duplicate either consent engine or invent a new real provider**.

## New narrow technical contract

`SyntheticSecondaryUseEvidenceGate` in the internal `Taranom.MemberGovernance` assembly is a pure test-only read-only evaluator. It receives synthetic `test:` member, data owner and consent-subject refs (all must match), tenant, exact proposed purpose ref and proposed data category ref; a positive independent-consent revision; and **seven independently supplied `EvidenceCheck` vectors**:

1. Current, independent member consent to the proposed secondary research/AI use.
2. Exact purpose, data category and consent scope matching the proposed use.
3. Current member identity / consent record binding.
4. Separately valid lawful processing authorization.
5. Effective protection of the relevant health data.
6. Explicit review of current withdrawal and continuing processing effects (no invented rule for revocation, old datasets, derivatives, or retention).
7. Auditable human governance and source provenance.

Never infer one from another or interpret care treatment/ordinary file sharing/subscription payment as research consent. Invalid or mismatched subject/scope returns `InvalidScope`. Any explicit negative check returns `ExplicitlyRejected`; missing or unknown/future check returns `EvidenceMissing`. **All seven caller-asserted Confirmed checks return only `EvidencePresentOnly`** — NOT allowed, ready for training, publishable, legally valid or Production-ready. This is a conservative *structural prerequisite exercise* and has no data fetch, token, grant, dataset builder, algorithm, external model API, telemetry or production code integration.

Any actual data use must separately verify real member consent, purpose and scope, lawful provenance, data protection, current revocation effects, independent human and legal policies, and actual clinical/AI governance. This test-only check cannot cause even a draft real dataset or model training run. No implicit permissible withdrawal/deletion semantics are invented.

## Member Web, disconnected

On `/member/consents` show four independent *unavailable* categories, each with `اطلاعات موجود نیست`: limited authorized treatment sharing; independent research/AI consent; separate lawful basis; and data protection/retention/withdrawal review. State there is NO verified consent, no member/patient file, and no permission to transfer data to datasets. The `ثبت یا تغییر رضایت` button is **disabled**. No default checked switches, fake consent history, real identity, medical information, external calls or background storage. Preserve CSP `connect-src 'none'`, no-referrer, noindex and all existing clinical protected-data DENY-ONLY controls.

## Tests and governance

xUnit: each of seven independent missing/rejected evidence dimensions, mismatched member and consent subject, cross-tenant and real-scope invalid refs, revoked/unknown review, policy missing, invalid enum, claimed everything never permits data, and care consent never fills secondary use. Three built Chromium cases: four unknown evidence categories, disabled mutation and zero private requests/no browser storage, navigation history. Run all **12 inherited CI jobs** including real PostgreSQL and center protected-data negative suites. Exact-head Code Review COMMENT + CI evidence, but no independent legal/medical/privacy/security approval. No Ready/Merge/Stage/QA/Release/Production.
