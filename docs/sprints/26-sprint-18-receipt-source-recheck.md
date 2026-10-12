# TRN-SP-18 — Close receipt request-binding and simulated revocation window

Process: Business TRN-BIZ-043/044/053/054 → Technical TRN-TECH-027 → Backlog → Sprint18 → Code → Code Review.

- BL-180: require the simulated server-side recipient source to answer for **both packet ID and exact receipt request ID**, with exact receipt evidence reference binding; prevent cross-request/other-reference acceptance.
- BL-181: run the same source check after the packet and exact disclosure-version row locks, immediately before a newly accepted receipt insert; missing/revoked/stale/changed issuer assertions return fail-closed and no new SQL rows.
- BL-182: preserve existing one-packet/one-receipt idempotency, immutable paired audit, correct concurrent retries, historical exact replay, stale conflict source rejection and no approval/permission vocabulary.
- BL-183: extend real PostgreSQL tests for changing test issuer, request/receipt pointer mismatch, attempted stale grant and changed recipient; preserve 165 inherited tests and twelve complete workflow jobs.
- BL-184: exact-head CI failure fixing, self-review, documented limitation that a double-check is NOT production grade transactional revocation or legal human receipt.

Exclusions: real reviewer authority/identity or issued grants, legal receipt, real delivery, clinical access, medical qualification, legal COI/recusal taxonomy, private data/consent, API/frontend, bank/insurer/wallet, Stage/QA/Release/Production. Keep stacked PRs Draft/Open; no merge.
