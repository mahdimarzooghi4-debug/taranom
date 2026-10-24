# TRN-TECH-168 — DRAFT / dependency-to-capability drilldown integrity
Date: 2026-10-17. Technical scope only, stacked after TRN-TECH-167. Not a Business approval, authorization for sensitive capability code, Stage, or Production.

## Source and boundary
Reuse **only** the already validated, read-only loopback `GET /internal/ops/v1/dependencies` from TRN-TECH-165/166. All 18 external dependency categories, 17 blocked domains and 39 provisional capabilities remain unverified; 26 entries reference nonproduction code/tests and 13 have no references. Never infer completion, priorities, owners, authority or percentages. National release stays `DENIED_BY_DEFAULT`.

## Testable bounded slice
- In the Persian Operations dependency workbench, an unresolved requirement can be selected from a row or dropdown to filter linked candidate capabilities. Epic and evidence-lane filters remain independently usable; resetting requirement restores all 39 candidates.
- Fail closed if a response claims a nonproduction reference count that disagrees with its affected Epic candidates, or if an Epic candidate claims a missing evidence category not identical to its related blocker requirements. Reject duplicates, dropped categories and altered readiness as before.
- Keep no client persistence, no command endpoint, no verified-identity/provider/clinical/financial/AI assumptions. Existing 14-job full regression, real local HTTP and Chromium browser checks remain mandatory. Injection or stale unverified data cannot become approval.

## Acceptance evidence to collect
Chromium must demonstrate requirement → linked candidates with exact filtered count, reset and release denial; tampered count/category must remove stale data and show unavailable; full CI exact-head green. A passing CI is not independent human Code Review or Release Approval.
