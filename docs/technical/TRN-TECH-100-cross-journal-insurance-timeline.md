# TRN-TECH-100 — Version-pinned cross-journal insurance dispute timeline

2026-10-13. Accepted Business: TRN-BIZ-049 (unified dispute coordination, insurer-only authoritative decisions), TRN-BIZ-051/052 (nonbinding cost awareness). Previous approved synthetic-only contracts: TRN-TECH-075/084/095/097/098/099. Parent PR #101 exact HEAD `9b91e59e198cab638c47ae33c72c34e3f5c41c93` with 14/14 Push and mainline PR CI green, main still only owner-merged through Sprint 86.

## User-facing gap and purpose

A synthetic support case had its own journal history and a cost preview had its own history, but the synthetic member did not have **one source-scoped, immutable-revision-pinned paginated timeline of the insurance dispute and its manually appended follow-ups**. An independent support history could be read using a different moment from the cost-preview choice, or pages could mix unrelated revision snapshots. Add a bounded synthetic-only member read path with no new persistence.

### Required single GET

`GET /internal/demo/v1/cost-previews/{previewId}/support-link/{caseRef}/timeline?afterRevision=<n>&limit=<1..20>&expectedCostChoiceRevision=<n>&expectedSupportRevision=<n>`.

Existing isolated localhost:4320 Kestrel demo and `TARANOM_COST_JOURNAL_DEMO=CI_EPHEMERAL_ONLY` remain mandatory; no public API. Exact canonical lowercase nonzero preview UUID and `test:` case ref, exactly four unique query parameters, strict unsigned ASCII decimal (no leading zero except literal 0), and limit 1–20. Reject unknown preview/support 404, incorrect member/topic/evidence/first event 409, changed pinned *either* source revision 409, invalid queries and excessive cursor 400, failure to replay canonical source/audit 503; preserve existing Host and forwarded-header restrictions. No cross-member details or timeline rows in denials.

Backend reads existing `PgSyntheticCostAwarenessJournal.ReadAsync` and `PgSyntheticSupportJournal.ReadAsync`, each validating all committed append-only events and audit lines through the unchanged canonical reducer. It verifies the original support event is unverified insurance dispute belonging to the *same synthetic member* and referring exactly to `test:cost-preview:<uuid>`; verified submitted actor is still only a claim, not authenticated identity. It selects a bounded support-history page (at most 20) of revisions, action labels and `test:` note references, checks both source revisions and cost preview fingerprint again immediately before returning. This is **independent read-only evidence**, not an atomic cross-journal transaction, and must state `atomicCrossJournalSnapshot=false`. Every insurer, finance, clinical consent and national release flag remains false. No clinical data, real insurer API or aggregate price is exposed.

### Member UI

The already isolated React member cost workspace displays the timeline **only after the separate verified insurance case/cost link**. Its explicit buttons get page 1 or next page using the same two pinned source revisions, preserve ascending event order and verify the server's exact source identities and denial booleans. On a 409, malformed page, lost connection or inconsistent result it clears any previously displayed pages, requiring human reinspection. Follow-up writes from Sprint 90 invalidate the old verified linkage; refreshing the link creates a new read-only timeline keyed to the observed revisions. Reload does not persist or automatically submit member history. Display is an unverified activity timeline, not insurer response, adjudication, legal service, financial entitlement or member authentication.

### Acceptance, no release

Actual .NET Kestrel/PostgreSQL HTTP tests: five real support events spanning 3+2 pages, changed independent cost source invalidating support page even without a support edit, support append invalidating cached cursor, unrelated member/evidence/medical topic, missing case/cost, invalid/duplicate query, out-of-range cursor, Host/forwarded protection, all authority flags false. Real Chromium: manual-only reads and recovery after reload; five events, pagination and stale support revision invalidation without fabricated outcome. Retain all 14 shared GitHub full-regression jobs; new exact Sprint91 Push and mainline PR-to-main CI on identical HEAD; record preliminary COMMENT technical review. Draft/Open/Unmerged; no merge, Stage/QA/Release Approval/Production without owner request.

External identity BFF TRN-TECH-012 is NOT CODE-AUTHORIZED; nationwide first comprehensive three-market launch DENIED_BY_DEFAULT; all 17 requirements BLOCKED_UNVERIFIED and completion UNBASELINED.
