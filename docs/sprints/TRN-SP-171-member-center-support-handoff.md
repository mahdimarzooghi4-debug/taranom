# TRN-SP-171 — DRAFT / manual member-to-center support-case handoff

Base: Sprint 170 Draft/Open PR #184, exact head `f55c1d9416ee66426972f63a1a973288a03e75a8`.
Business grounding: TRN-BIZ-045/046 and member-centered coordination without autonomous clinical authority.
Technical contract: [TRN-TECH-180](../technical/TRN-TECH-180-member-center-support-handoff.md).

Deliver an actual cross-interface *synthetic* workflow: source-pinned case locator export from Member Support, strict versioned locator verification in Center QA, independent GET rehydration from the original support PostgreSQL journal, and the existing audited manual follow-up/history. No new server route, new schema, impersonated login, automatic referral or out-of-band records. Test with real isolated ASP.NET + PostgreSQL + both separately served Chromium apps; negative cases for forged/extra-authority, unrelated case, stale revision and fake backend authority. Canonical full 14-job CI and initial exact-head technical Code Review COMMENT. Keep all PRs Draft/Open/Unmerged and all Stage/QA/Release/Production gates denied. National official launch DENIED_BY_DEFAULT.
