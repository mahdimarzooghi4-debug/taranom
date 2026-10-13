# Sprint 100 — Support-case MVCC reader safety and real lock contention

**Date:** 2026-10-13 | **Technical:** [TRN-TECH-109](../technical/TRN-TECH-109-support-mvcc-read-consistency.md) | **Business:** TRN-BIZ-045/048/049 | **Epics:** TRN-EP-10/12/13.

Parent PR #110 Draft/Open `5a1f2f33ef3bd79207c176d02248373b1ae2e0ca`, exact Push #38385841234 and Mainline PR #38385846487 SUCCESS **14/14**, initial COMMENTED code review only. Approved complaint/insurer member follow-up remains an **unverified synthetic journal**, never official insurer/medical approval.

**Concrete systemic defect:** existing support GET takes the same PostgreSQL support header `FOR UPDATE` row lock as mutations. It can unnecessarily wait on a writer. Add consistent nonlocking `REPEATABLE READ READ ONLY` MVCC rehydration to ALL existing support HTTP current/history/point-in-time reads; preserve original writer's row lock, CAS, exact UUID replay, immutable event + paired audit, SHA256, fail-closed history and no-rights flags.

**Acceptance:** real Kestrel+PG C# probe holds uncommitted row UPDATE setting revision=77, verifies a concurrent HTTP reader returns the last **committed** revision 1 before rollback, tests 10 accepted follow-ups with competing readers and exactly 11 audited events. Existing HTTP/Chromium/support workflow, Member/Center integration, security/recovery and 14-job Push/Mainline PR CI remain. New direct test invocation within current 14-job CI, no bypass; initial technical review COMMENT only, not independent human approval.

No real identity, clinical complaint, insurance decision, provider contract, finance, PHI, Production, Stage or release. Real Member BFF NOT CODE-AUTHORIZED, 17 launch gates BLOCKED_UNVERIFIED, comprehensive national first release DENIED_BY_DEFAULT, completion UNBASELINED. Keep all PRs Draft/Open/Unmerged.
