# TRN-TECH-200 — DRAFT / Cancelable GET-only Member coordination reads with exact response-generation isolation

Date: 2026-10-23. Applies to the existing TRN-BIZ-046/047 synthetic nonclinical Member/Center workflow. Preserves TRN-TECH-115, 185, 189, 194–199, all 13 authority denials, exact command UUID/ACK replay and independent PostgreSQL attestation.

## Source-backed functional gap
The Center care workspace already fences late responses with an incrementing request generation, but the Member workspace's independent source GET, Center-return reattestation GET and filtered version-pinned history GET had no equivalent fence. A delayed response could complete *after the user had deliberately moved on* and repopulate an old synthetic case, obsolete returned-claim receipt or stale event history. Since busy disables editing while GET is in progress, the Member tester also had no safe way to abandon a slow **read-only** operation without abandoning the page.

Deliver a visible **manual Cancel read-only operation** for exactly three existing Member GET paths: canonical independent case `READ`, manually pasted Center→Member claim-return reattestation, and first/next `TRN-TECH-195` pinned history GET. Cancellation is a local UI revocation, not a server undo, AbortController network cancellation claim, audit deletion or POST cancellation. It releases the Member view so the tester may select another synthetic case and independently read its source. It must clear old actionable source, activity, historical as-of selector, latest page, and confirmed returned locator, then show a status explicitly stating no command was executed. The in-flight HTTP promise may physically complete, but **none of its late success, error or finally branches may mutate the current UI**.

## Concurrency model and exact governance
Maintain a synchronous monotonically increasing `useRef` generation, incremented when any cancelable read begins, a read is canceled, the case/member/coordination inputs or manually pasted return locator change, an independent recovery ticket is loaded, a new exact UUID command starts, or a filter selection changes. Every GET-only response captures its generation *and scope* before network; it checks the generation after full response parsing and before committing any source, counts, page, return confirmation or status, and suppresses stale catch/finally state updates. A stale failed HTTP 503 must not erase a newer successfully read case or overwrite its status. Cancellation is exposed ONLY while one of the three GET-only actions is active; it must never appear for an in-flight POST, unknown command acknowledgement, or GET-only exact-UUID recovery of a potentially committed POST. The existing `uncertain` latch and manual exact UUID replay/receipt must remain intact.

No auto-POST, new backend endpoint/route/schema/domain event, authentic member identity, clinician permissions, actual records, referral, notification, polling, cookies or persistence. All user-facing identifiers bounded to ephemeral `test:`. The source's PostgreSQL journal replay, SHA-256 audit binding, response denials and strict data contracts are unchanged.

## Acceptance and CI
Five true Chromium/Kestrel/isolated PostgreSQL scenarios with intentionally **held** HTTP responses:
1. Hold valid A case source GET, cancel it, manually read B, return A success late, verify B remains the only accepted source and zero browser POST.
2. Hold source-verification of a syntactically valid Center claim-return for A, cancel, read B, deliver A late, ensure no prior return confirmation or wrong-case action.
3. Hold pinned care history GET, cancel and read B; late page cannot restore stale as-of/history UI.
4. Hold failed A source GET (503), cancel and read B; late failure cannot overwrite B status or source.
5. Lost POST ACK after *actual* journal commit has no Cancel control; original UUID/receipt and uncertain command lock remain, and no duplicate commit occurs.

Full canonical 14-job exact-head CI (real Chromium/Kestrel/isolated PostgreSQL + restore); preliminary Code Review COMMENTED only, independent human review outstanding. This is a safety and operational workflow feature, not Stage/QA admission, clinical approval or release. Keep stacked PR Draft/Open/Unmerged. Official initial comprehensive simultaneous nationwide 3-market launch DENIED_BY_DEFAULT.
