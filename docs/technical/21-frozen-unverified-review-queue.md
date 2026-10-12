# TRN-TECH-021 — Frozen unverified-review queue membership under concurrent writes

**Date:** 2026-10-10. **Status:** test-only internal PostgreSQL implementation. Business basis TRN-BIZ-053/054 and TRN-BIZ-043/044. Previous Technical TRN-TECH-018/019/020; stacked PRs #16–#18 remain Draft/Open. No independent source of real legal authorization, physician license, clinician qualification, Taranom provider contract, reviewer role or real health-data consent is selected.

## Problem addressed

The Sprint 11 keyset queue is a *live* query: an insert whose sort key is before the current cursor is excluded and a later-keyed insert may appear mid-traversal. Neither promises an exhaustive consistent set. That live query remains intact and is **not silently rebranded snapshot safe**.

Sprint 12 adds a **distinct opt-in frozen membership API** inside the existing **internal non-packable test-only** Npgsql journal assembly:

1. `PgFrozenUnverifiedReviewQueue.CaptureAsync(center, kind?)` uses PostgreSQL 18 `REPEATABLE READ` **one transaction**, SELECTs the entire synthetic pending review queue for precisely one center/optional subject category, validates each row against matching accepted event/request JSON, paired pending work-item and paired accepted audit, then stores the exact ordered immutable membership as one JSON manifest with SHA-256 consistency digest and generated snapshot UUID in **the same transaction**. Invalid/incomplete claims abort capture.
2. `PageAsync(center, kind?, snapshot_id, pageSize, cursor?)` reads **only the captured manifest**, verifies its digest, count, ordering, synthetic identifiers, center and optional subject filter; pages using an index bound to that snapshot. New evidence written after capture, whether before or after an alphabetic cursor, cannot affect the captured results. A new capture is needed to see later submissions.
3. `FrozenReviewCursor` contains ID, center, subject filter and the next index. Invalid/mismatched scope or offset fails closed. IDs and cursors are strictly **test-local/unsigned and NOT bearer credentials, real reviewer authorization, public wire contracts or unguessable source-ownership proof**. No expiry/retention duration or cleanup rule is invented.
4. New isolated `internal_test_frozen_review_snapshot` is insert-only append-guarded against ordinary update/delete. A distinct **ephemeral restricted snapshot role** receives SELECT on four review source tables and INSERT/SELECT **only on the snapshot table**; it cannot mutate accepted events, evidence, reviews or audit. The previous queue's SELECT-only test role remains separate and unchanged.
5. This materializes *all members* with no production volume or memory benchmark. The manifest duplicates **synthetic references only**, not real documents, national IDs, physician licenses or medical data. An SQL administrator/superuser can still alter triggers, checksums and schema; non-keyed SHA-256 is NOT a legal signature or immutable external attestation. No actual reviewer session, service identity, RLS, regulatory human decision or real center data access has been activated.

The as-of guarantee is **membership/order captured by one PostgreSQL REPEATABLE READ transaction**. It does not guarantee later real-world validity of any claim, clinician/manager qualification, a snapshot-wide legal status, processing exactly once, fair queue assignment or ongoing rights. Freeze is not a workflow transition or review approval. Previous Sprint 08 protected-data Interlock stays deny-only.

## Acceptance

Expand the **existing** genuine PostgreSQL integration job with tests for: no-record snapshot frozen across later additions, inserts with lower/higher sort keys, same doctor's separate qualification/contract claims, category filtering, cross-center/cross-filter/cross-snapshot cursor rejection, empty/missing/invalid snapshot, technical page size bounds, captured revision replay, restricted SQL principal and immutable trigger, corrupted source failure to capture. Preserve the previous 39 review-journal and queue tests and all inherited twelve CI jobs (.NET, real PostgreSQL, Member and Center Chromium).

No invented medical/professional rule, human-review approval semantics, official professional source, reviewer access role, file storage, external integration, web/API endpoint, active clinician, wallet/insurance or network parent panel. Independent Code Review required; Draft/Open and no Merge, Stage, formal QA, Release or Production.
