# TRN-TECH-095 — Durable synthetic cost-awareness vertical slice with real PostgreSQL

**Sprint 86 — Technical / Code Authorization for test-only execution.** Based on accepted TRN-BIZ-051 and TRN-BIZ-052, and the already implemented `PgSyntheticCostAwarenessJournal` (TRN-TECH-038), `SyntheticCostPreviewBuilder` (TRN-TECH-093) and `CostAwarenessRecorder` (TRN-TECH-094).

## Meaningful outcome

The prior localhost:4318 five-source inspection and in-memory choice replay can be tested without database writes. Sprint 86 **does not** invent a second journal: it exercises the already implemented immutable PostgreSQL five-source preview registration and append-only choice/audit tables from a separate same-origin **local CI-only** browser. A tester can register a `test:` five-source preview, record an unverified choice, deliberately replay the exact command UUID, refresh the browser, and read the same database snapshot by UUID. No browser storage; all writes are manual. This is the first cost-choice UI testing actual durable database behavior, not a new read-model/mock.

## Bounded architecture

The existing `Taranom.CenterWorkflow.Demo` process remains strictly localhost port 4320, using the already enforced exact Host and rejection of all forwarded authority. It starts only with `TARANOM_CENTER_DEMO_TEST_ONLY=CI_EPHEMERAL_ONLY` and existing isolated `TARANOM_TEST_PG` pointing to `taranom_sprint64_center` on local 5432. Within that **already isolated** process, a further explicit opt-in `TARANOM_COST_JOURNAL_DEMO=CI_EPHEMERAL_ONLY` enables the cost table initialization and exactly three test-only HTTP routes:

- `POST /internal/demo/v1/cost-previews`: exact canonical five-source preview input, immutable register-or-exact-replay; divergent immutable revision/member/amounts/source under same UUID returns 409.
- `GET /internal/demo/v1/cost-previews/{previewId}`: reattested current test-only preview identity and unverified choice revision, including full existing event/audit verification. Unknown UUID returns 404 rather than an invented empty member record.
- `POST /internal/demo/v1/cost-previews/{previewId}/choices`: original `CostAwarenessRecorder` command with exact UUID, expected revision, preview UUID/revision/fingerprint and one of the three approved synthetic choice values; command stored in the existing append-only PostgreSQL event/audit in one transaction. Exact retry returns `Replayed` without duplicate. Conflicting UUID, stale revision and changed preview are 409; missing source is 404; invalid input 400; broken journal fails closed 503.

No broad `InternalsVisibleTo` grant: **only** the existing isolated test host `Taranom.CenterWorkflow.Demo` receives scoped friend access to the PostgreSQL journal assembly. MemberGovernance already permits that demo assembly for other canonical test-only contracts. There is no new ORM, schema, business rule, production database, financial adapter, external service, real member account, authentication or legal signature.

All POSTs require exact Origin `http://127.0.0.1:4320` or `http://localhost:4320`, disallow missing/foreign Origin, enforce 8192/1024 byte bounded JSON bodies, exact top-level and nested keys and duplicate rejection, bounded depth and strict parsing. All public output explicitly asserts false for authenticated member, insurer entitlement, authoritative estimate, official choice/legal signature, wallet mutation, clinical consent, financial commitment, and national release. Data are exclusively `test:` and opaque UUIDs, without names, diagnosis or real insurance/payment claims.

## UI and failure behavior

Only the isolated member demo bundle at `/member-demo/costs` on that same localhost server exposes the experimental form. It starts with five truly `Missing` shares (no default zero). A human may independently set a lane to `ProvisionalClaim` or `ConflictingClaims`: conflicts have no amount. No total, service tariff, coverage, member payable remainder, debt or wallet balance is computed. Register and record require distinct explicit clicks. After a confirmed registered preview, the choice command is pinned to exact preview fingerprint and server choiceRevision. Under an uncertain/lost response the browser freezes the exact command (same UUID and fields) and disables further registration, reading or new choice until the user retries the same command. The database's original idempotency policy handles either case. Reload does not automatically post or store member data; manual UUID input plus GET verifies persistent state. The existing `/member/costs` official-member informational view and its disabled legal controls remain unchanged.

Urgent medical referral is never gated by the form. Every result is labelled synthetic/nonbinding. A customer, insurer, clinical provider, employer or support institution receives no rights from this demonstration.

## Real evidence and release gates

Add genuine HTTP + PostgreSQL tests covering immutable registration, five independent states, exactly-once replay, stale/modified/forged commands, unknown preview, two concurrent simulated writers (one commit and one stale response), scoped Origin/Host, exact JSON schema, malformed or oversized bodies and rights denial. Real Chromium tests cover register → choice → exact replay → reload → UUID read, immutable version conflict, and network acknowledgement lost **after PostgreSQL commits**. Run these within the existing `ephemeral-center-workflow-full-stack` job and retain all **14 shared full regression jobs**. The new dispatcher is exclusively allowlisted for branch `sprint-86-postgres-cost-choice-workflow` and PR base `sprint-85-synthetic-cost-choice-continuity`.

All 17 national launch blockers remain `BLOCKED_UNVERIFIED`. First launch must still be comprehensive across all three markets, `DENIED_BY_DEFAULT`, completion `UNBASELINED`. Real member Login/Session BFF TRN-TECH-012 remains NOT CODE-AUTHORIZED. Keep Sprint PR Draft/Open/Unmerged, no Ready/Merge/Stage/QA Gate/Release Approval/Production or human-approval claim.
