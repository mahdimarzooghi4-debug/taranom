# TRN-TECH-175 — DRAFT / Manual cross-department affiliation adjustment in the center workspace
Date: 2026-10-18. Internal test-only operational UI increment; no new business decision, verified clinician, center-manager login, clinical placement, scheduling or release.

## Approved scope and existing implementation
TRN-BIZ-053/054: a center owns its multiple departments and manages its unverified doctor affiliations locally; there is no independent network or chain parent. The canonical center PostgreSQL event/audit journal already provides `DefineDepartment`, `RecordDoctorAffiliation`, `AssignDoctorToDepartment` and `UnassignDoctorFromDepartment` with versioned CAS and exact UUID replay (TRN-TECH-073, 074, 093, 108). Sprint 99 verified that unassignment only affects one relationship and never ends the entire affiliation.

## Product capability delivered
In the existing Persian center operator test workbench, add a deliberately non-atomic, human-guided two-action adjustment between two **existing departments of the same center**. The user selects a source department using the existing field, enters a distinct target department, and chooses the same unverified doctor. The guidance derives only from the latest validated PostgreSQL center snapshot:
- Require all refs `test:`, source and destination different and *already defined* in that exact center, and a doctor with a currently pending-independent-verification affiliation assigned to the source.
- If destination is not assigned, offer a separate human-triggered `ASSIGN_DOCTOR` targeting destination. Its own new request UUID and expectedVersion are sent through the existing original CAS journal. No multi-command loop.
- Only when the next source snapshot proves destination assigned **and** source still assigned, offer a second separate human-triggered `UNASSIGN_DOCTOR` on source; do not auto-call it after step one.
- If source is already absent and target exists, show only the observed target membership, never claim this page executed both actions. Ended affiliations, missing department, same department, out-of-center source or missing snapshot refuse actions.
- If a request is stale, conflicting or HTTP ACK uncertain, existing exact-request persistence/replay logic remains responsible; no next-step write is available while unresolved. There is no atomic guarantee: between actions the doctor may be affiliated with both departments.

No backend route/schema/permission/clinical state or real data source is introduced. The browser remains an explicitly unauthorized, synthetic-only local test client. This is not a staffing policy or real clinical placement. No transfers between centers.

## Acceptance
Three real Kestrel/PostgreSQL/Chromium regressions: independently posted two commands produce exactly two new journal events, with intermediate two-department assignment and final single destination; another concurrent writer changes revision and stale UI cannot create duplicate; invalid target and dropped acknowledgment preserve pending latch and require exact manual replay, no automatic second write. Keep original canonical 14-job full regression and exact-SHA technical COMMENT review. No independent approval, Stage, QA Gate, Release, Merge or Production; national first release DENIED_BY_DEFAULT.
