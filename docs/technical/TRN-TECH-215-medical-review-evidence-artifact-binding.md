# TRN-TECH-215 — Bind review evidence bytes to recommendation events

## Contract

- `MedicalSourceReviewSubmission` contains no evidence digest field. `AppendWithStoredEvidenceAsync` verifies the source package, stores the supplied evidence stream, verifies the resulting blob, derives the evidence SHA-256 internally, and adds it to the serialized review command before append.
- Event hash and audit hash therefore cover the computed evidence digest as part of the canonical command JSON.
- A missing or altered source/evidence blob fails before event insertion. Existing event/audit/head writes remain one PostgreSQL transaction.
- Claimed evidence references remain caller claims. The service does not parse evidence or infer legal/scientific approval.

## Limits

CI uses synthetic source/evidence bytes and temporary storage. Filesystem writes and PostgreSQL append are not distributed-transactional; an unreferenced blob may remain after a later failure. Authentication, evidence custody, qualified human review, retention, production storage, training, clinical use and Production remain unimplemented or denied.
