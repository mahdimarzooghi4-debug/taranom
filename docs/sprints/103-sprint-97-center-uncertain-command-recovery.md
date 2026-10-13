# Sprint 97 — Human-controlled recovery of uncertain center commands

**Date:** 2026-10-13 | **Technical contract:** [TRN-TECH-106](../technical/TRN-TECH-106-center-uncertain-command-recovery.md) | **Business:** TRN-BIZ-053/054/028/030/040 | **Epics:** TRN-EP-04/12.

Parent PR #107 Draft/Open exact HEAD `d88f8b80db3dcdc40448500e76ad93b33b4418a4`; Push #38372216944 and PR #38372222783 **14/14 SUCCESS**; preliminary COMMENTED review, no human approval. PR #98–107 remain Draft/Open/unmerged.

**Business-facing defect:** after the test-only center's real PostgreSQL write succeeded, a lost HTTP response made the browser state unknown; a later button used a new request UUID, so recovery could not be attributed to the original write. The center is not an authenticated licensed provider.

**Implementation:** rework the existing four manual center commands/guide to retain the **one original body** on uncertain response; block all new writes and changes to context; display manual human retry using the same UUID/expected revision; keep snapshot and history cleared until conclusive verified response; allow read but never use read to dismiss uncertainty. No automatic retry or persistence, no additional endpoint/DB/UI portal; do not conflate transport uncertainty with failure. Browser restart loses the receipt and is explicitly warned.

**Acceptance:** test genuine commit then ACK loss, before-commit loss, later independent writer/stale rejection, invalid HTTP acknowledgment, no duplicate or silent auto-post, no clinical rights; supplemented by late replay HTTP test and full 14-job CI. Review only COMMENT; no Merge/Stage/QA/Release/Production. Seventeen national launch gates remain blocked, release denied by default, completion unbaselined.
