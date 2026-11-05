# TRN-TECH-210 — PostgreSQL persistence for medical source review recommendations

## Boundary
Sprint 200 adds an isolated PostgreSQL journal for the offline medical review workflow. It stores metadata fingerprint references and reviewer recommendations only. Article/evidence bytes, real identities and clinical decisions are not stored or fetched.

## Contract
- Open a bounded catalog by its existing catalog/taxonomy SHA-256 claims and source/package fingerprints. The adapter pins scope; it does not independently validate source bytes or establish ownership, license terms, scientific quality or taxonomy correctness.
- Append only REQUEST_EVIDENCE, RECOMMEND_ACCEPT or RECOMMEND_REJECT in RIGHTS, SCIENCE or TAXONOMY, using a row lock, request idempotency, expected revision, transactionally coupled event/audit rows and head compare-and-swap.
- Read verifies contiguous revisions, source/package scope, the event hash chain, audit correspondence and head projection. Database triggers reject changes to source/event history and audit rows.
- Trust and authority indicators remain false. Reviewer and evidence references remain caller claims.

## Limits
The schema uses internal_test tables and synthetic test: source refs. It provides an ephemeral PostgreSQL contract in CI, not a production deployment or access-controlled reviewer service. Authenticated reviewer qualification, independent evidence custody, legal/scientific decisions, taxonomy authority, production migration and owner approval remain separate work. Clinical use, training and Production remain denied; national release stays DENIED_BY_DEFAULT.
