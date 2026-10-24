# TRN-TECH-206 — Sprint 197: medical literature source catalog + synthetic specialty-choice QA

## Approved product boundary
The member may receive **non-diagnostic support choosing an appropriate physician**, but **selects the physician personally**. No AI medical diagnosis, prescription or autonomous physician choice. Medical safety, urgent-care handling and human specialist review remain independent gates. This package is an internal technical foundation; it does not authorize real patient advice, real provider claims or Production.

## Implementation
- `ai/taranom_internal/medical_knowledge.py` freezes an immutable-by-value, order-invariant SHA-256 fingerprinted **bibliography metadata** manifest (not article bodies). Each record has a bounded title, publisher, HTTPS reference URL, asserted license label, rights-evidence reference and *declared* SHA-256 for source content. The module does not fetch URLs or validate source bytes/license rights; every imported record remains `rights_verified=false`, `scientifically_reviewed=false`, `training_allowed=false`. The ref must be `pub:` or `test:`, and malformed, duplicate, local/IP and non-HTTPS citations fail closed.
- A separate `TARANOM_KNOWLEDGE_QA_ONLY=CI_EPHEMERAL_ONLY`-gated routing harness accepts **only `test:` fabricated bibliography, specialty-review links and provider registry refs**. It supports normalized Persian words, word boundaries, deterministic **unranked** specialty/provider options and explicit source/review provenance; no new medical mapping, clinician identity or threshold was invented. Provider labels must start with `test:`.
- Every result keeps `diagnosis_provided=false`, `doctor_selected=false`, `clinical_use_allowed=false`, `rights_verified=false`, `provider_verified=false`, `production_allowed=false`, `inference_supported=false`. No real patient query storage, inference endpoint, data transfer, downloaded model, clinical/safety gate or real registration. No claim of physician quality/"best" or trained medical AI.
- The module is Python standard-library only, isolated from the existing offline AI registry and all real .NET owner tables. Its boundaries explicitly prevent an unreviewed public article becoming training input or public recommendation.
- `tests/ai/test_medical_knowledge.py` is automatically discovered by the existing 14-job CI under `python3 -m unittest discover -s tests/ai -p 'test_*.py' -v`. Includes manifest determinism/tamper/rights denials, HTTPS/duplicate hardening, normalized Persian fictional matching, ambiguity/no-match/no-provider, clinical authority and real-provider identity rejection, test environment isolation.

## Remaining engineering work (not faked by this Sprint)
1. Real bibliographic rights and source-byte verification pipeline, source-specific licensing, retraction/version reconciliation, source retention, independent review workflow, curated terminology/taxonomy and clinical validation.
2. Independently approved symptom→specialty evidence mappings and patient-safety/urgent-care protocols. These **cannot** be inferred from titles or simple lexical matches.
3. Legitimate licensed provider/clinic registry with verified specialties, geographic coverage, operational capacity and tenant access controls; no real provider exists in this QA harness.
4. Authorized .NET admission/consent/access controls, authenticated member Web/Android flow, artifact governance, medical evaluation and internal model selection. LLM/embedding/RAG family, model weights, performance thresholds and GPU sizing remain undecided pending evaluation.
5. External providers, servers, approvals, Stage/QA Gate, Release and Production are untouched. All 17 national release blockers remain `BLOCKED_UNVERIFIED`; `DENIED_BY_DEFAULT` continues.

## Evidence and limits
This change is a **meaningful executable library and test package**, not a deployed AI assistant or clinically approved physician-matching capability. Rights evidence is a declaration; source hashes are declarations unless checked against bytes by a separate authorized operation. Human-reviewed schema and real scholarly source imports can be implemented next without collecting any patients' medical data.

## Metadata-only search
`search_bibliography(snapshot, phrase)` now searches **only frozen bibliographic titles and publishers**, returning deterministic bounded references to catalogued source URLs, not medical recommendations. Search never fetches article content or stores query history; rights and clinical approvals remain false. The synthetic routing harness is a separate and stricter test-only boundary.
