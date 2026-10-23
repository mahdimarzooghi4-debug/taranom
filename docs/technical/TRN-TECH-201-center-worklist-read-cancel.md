# TRN-TECH-201 — DRAFT / Cancelable Center worklist paging and independent row audit

Date: 2026-10-23. Scope: existing synthetic, nonclinical Center coordination worklist TRN-TECH-186/192/193 and Member request/claim journal TRN-TECH-115, plus Sprint 191 GET cancellation convention TRN-TECH-200. Business constraints TRN-BIZ-035/041/043/045/046/047 unchanged.

## Actual operational gap
The Center's existing bounded four-case worklist supports human-triggered paged list GETs, same-page independent activity comparison, and a sequential independent GET audit of each of four canonical PostgreSQL care journals. While these GET-only operations are in progress, the page is locked but there is no manual cancellation. An operator cannot discard a slow page or audit to select another query. More importantly, a four-row sequential audit can continue issuing additional independent case requests even after the old view is no longer wanted. Parent Center actions can start while a child worklist read is in flight; such a read should no longer be allowed to select a stale case afterward.

## Implementation contract
Add **one operator-clicked local cancellation** action only while the Center worklist performs one of its three read-only operations (page/next page, same-page deltas, per-row four-case audit). Cancellation increments the existing synchronous `useRef` generation, clears rows, pagination continuation, activity, audit, baseline cursor and readiness, and releases the query/filter controls. The asynchronous old GET is not claimed to be aborted at the HTTP/TCP/PostgreSQL level, but its late successful **or failed** response must not re-authorize old rows, restore activities/partial audit, or overwrite a newer query state. The first still-in-flight audit GET may physically finish; once its response arrives, the audit loop must check the generation and stop without issuing GETs for any remaining rows. No partial audited result may be displayed.

When the parent Center workspace transitions to busy/pending because it is reading/performing/recovering an original case action, the child worklist invalidates its old baseline and current/late read results. The worklist Cancel control must never appear or be usable to alter the parent's command POST, exact immutable UUID/payload, uncertain ACK lock, read-only reconciliation or manual exact replay. Worklist cancellation is not a clinical case closure, actual notification, read receipt, member/provider assignment or resource release, and performs **zero POSTs**.

Strict existing `TRN-TECH-186` response schema/13 false-authority flags, per-row exact source journal attestation, original source event/audit verification, bounded page size and fail-closed page/count/membership checks remain untouched. No added API route, server model, PostgreSQL table, data retention period, entitlement, medical inference, authority or Production behavior.

## Adversarial acceptance
Five genuine Chromium/Kestrel/isolated PostgreSQL tests must prove:
1. Hold an actual four-row A worklist response; cancel and open B, deliver A late, B remains the only source-validated, selectable result and no POST.
2. Hold a denied HTTP 503 for A; cancel and read B, late error cannot clear B or overwrite its status.
3. Hold first per-row independent case GET during a four-case audit; cancel/re-baseline to B; when the old first GET returns, the other three old-source GETs are not issued, no partial audit is accepted.
4. Hold same-page delta response; cancel, change filter, late data cannot restore old rows/activity.
5. Start a real Center POST with an intentionally lost ACK while a worklist GET is pending: the parent disables/invalidate the worklist and hides Cancel; original POST is committed once and retains the exact uncertain UUID manual recovery gate.

Run the canonical full 14-job CI for the exact final HEAD, including Chromium/Kestrel/PostgreSQL and database restore, and submit technical Code Review COMMENTED (not independent human APPROVE). Preserve all stacked PRs Draft/Open/Unmerged, do not run Stage/formal QA/Release Approval/Production, and retain comprehensive simultaneous first nationwide three-market rollout DENIED_BY_DEFAULT.
