# Sprint 93 — Versioned department physician roster, original center Journal

Date: 2026-10-13. **Approved Business:** TRN-BIZ-053 and TRN-BIZ-054; **Technical:** TRN-TECH-102. **Parent:** Sprint92 Draft PR #103 HEAD `bbb36531914e0afcc50be3ff3bf3b6e226ea9789`, Push and PR full 14/14 SUCCESS.

Build genuine test-only read capability around the **existing** event-sourced isolated PostgreSQL center: one center with multiple departments and physician affiliations. Return deterministic and version-pinned doctor roster per requested department with historical reconstruction from canonical reducer. Ended affiliation has no current roster assignment but earlier versions remain immutable. Add RTL manual read UI and deny-only clinical/legal/financial rights. No additional database or auth adapter, provider licence, contract, network administration, patient record, wallet or real doctor.

Acceptance: Kestrel+PostgreSQL positive/negative, concurrent/stale, multi-center isolation, immutable historical replay; real Chromium click-only read, no automatic write or fabricated rights; unchanged full 14 GitHub CI jobs with new test under Sprint92 test-source census; preliminary Code Review COMMENT. All stacked PRs Draft/Open/Unmerged. 17 official national readiness gates BLOCKED_UNVERIFIED; first comprehensive national release DENIED_BY_DEFAULT. No Stage/QA Gate/Release/Production.
