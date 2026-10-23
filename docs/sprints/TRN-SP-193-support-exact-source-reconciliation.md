# TRN-SP-193 — DRAFT / support command integrity across Member and Center

Stacked on PR #206 exact parent `199c87fa72e7afecb0f9330ef90fffc439494c88`. Technical: [TRN-TECH-202](../technical/TRN-TECH-202-support-exact-source-reconciliation.md).

Close a core operational risk: both test-only support web surfaces must check original immutable UUID from an independent PostgreSQL journal GET, not simply trust a 200 POST. Permit explicit GET-only recovery of genuinely committed uncertain commands; preserve lock for missing or mismatched events. No new backend API, database change, real support rights, auto replay or national launch. Add real Chromium/Kestrel/PostgreSQL adversarial regression; keep Draft/Open/Unmerged until full 14-job CI, COMMENTED technical review only. No human approval or deployment.
