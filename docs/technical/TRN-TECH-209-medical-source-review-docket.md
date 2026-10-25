# TRN-TECH-209 — Offline medical source catalog and review recommendation docket

## Boundary
Sprint 199 extends TRN-TECH-208. This module is an **offline, metadata-only, non-production review preparation component**, not a legal authorization system, clinical approval system, document store, medical knowledge corpus, or trusted audit service. Public/free-to-read is not permission to reuse or train.

## Contracts
1. freeze_source_review_catalog accepts a bounded list of source packages with caller-supplied article and rights-evidence bytes and a frozen taxonomy. It invokes the existing byte-integrity verifier for each package, rejects duplicate source/intake refs, sorts entries deterministically, and returns only article/evidence SHA-256 and citation/taxonomy metadata. It does not persist, index, retrieve or emit source bytes.
2. open_source_review_docket validates catalog and taxonomy fingerprints, then opens independent RIGHTS, SCIENCE, TAXONOMY work items per source, initially PENDING. No categories, reviewer identities, assignments, license interpretations or scientific decisions are invented.
3. append_review_recommendation records only REQUEST_EVIDENCE, RECOMMEND_ACCEPT or RECOMMEND_REJECT with exact package SHA, source, lane, claimed reviewer ref, claimed evidence ref and expected revision. Event IDs are idempotent; conflicting replay, stale revision, invalid lineage and tampered hash chain/projection fail closed. Events are append-only in returned snapshots; caller is responsible for actual transactional persistence.
4. source_review_worklist exposes review statuses only. Even when every lane recommends acceptance, rights_verified, scientifically_reviewed, taxonomy_reviewed, training_allowed, clinical_use_allowed and production_allowed remain false.

## Trust and security limitations
- Claimed reviewer/evidence refs are **not authenticated**. A SHA-256 hash chain is an integrity check for accidental/naive modification, **not** a digital signature or independent custody attestation; a malicious caller could forge an entire recomputed docket. This component cannot certify that a qualified reviewer made a decision.
- The returned snapshot is in-memory. It is not an append-only database, atomic multi-writer store, legal approval, medical review, source-content ingestion, model training or Production activation.
- A real workflow must independently provide authenticated reviewer identity and qualification, independent evidence custody, immutable storage, concurrency control, legal/scientific decision policy, source version/retraction handling and explicit human approval before changing any authority flag.
- Only synthetic test bytes/metadata are used. No real patient, physician or copyrighted source content is committed. No URL fetch, external AI API or inference.

National release admission remains DENIED_BY_DEFAULT.
