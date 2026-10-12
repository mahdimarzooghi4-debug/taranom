# Taranom integration through Sprint 86 — owner-authorized code merge

**Date:** 2026-10-12. **Status:** Integration candidate pending exact-head 14-job Push and PR CI, GitHub merge result, and explicit post-merge verification. **Target:** `main`.

This is a repository code integration, **not** a Stage, QA Gate, national release approval or Production deployment. National first official launch must cover all three markets and remains `DENIED_BY_DEFAULT`; all 17 national release blockers remain `BLOCKED_UNVERIFIED`; overall product completion remains `UNBASELINED`.

Integration commit ancestry preserves the full original mainline from Business PR #1, through the successive Technical/Sprint branches and Sprint 86 PR #96 at `4b705d16ad6083b4bf26bcde3c7d90bb6c31246e`. It also preserves the independent branch ancestors for #10 (approved center actor hierarchy), #62 (commit-message credential safety), #89 (test-only payer contract what-if) and #93 (guided four-event test center workflow). The overlapping files in these parallel branches were reconciled conservatively on the tested latest mainline rather than replacing newer security boundaries.

The integration branch has its **own exact-name, read-only Push and PR dispatcher** for the existing single canonical full 14-job regression workflow; the public CI policy guard names the dispatcher, head and main base exactly. It must be green before merging.

Existing Sprint PRs were originally Draft and their automatic COMMENT reviews do not constitute independent professional approval. The owner has explicitly directed repository code merge. Formal review evidence, real identity/session authority, clinical and financial authorizations, privacy and external infrastructure remain separate blocking gates. No self-promotion, money, external AI, real healthcare integration or public API is activated by merging.
