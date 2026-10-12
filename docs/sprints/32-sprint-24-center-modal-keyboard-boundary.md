# TRN-SP-24 — Center modal focus and disconnected safety

**Date:** 2026-10-10. **Base:** `sprint-23-synthetic-receipt-audit-sweep` (Draft PR #30).

Business TRN-BIZ-053/054 → TRN-TECH-033 → Backlog → Sprint24 → Center Web code → Chromium tests → twelve-job CI → preliminary code/security review.

- BL-240: ensure the Center Web action dialog's `aria-modal` claim is true for real keyboard focus via modal containment and inert background.
- BL-241: close mobile navigation on action open and close stale dialog on Back/Forward; restore focus to trigger on Escape/dismiss or route heading after navigation.
- BL-242: maintain disabled write actions, zero private API calls and no synthetic authority/clinical access.
- BL-243: add built-Vite Chromium tests of all four nonmutating modal actions plus focus, history and mobile regressions.
- BL-244: run inherited 12-job CI at exact SHA, document failures/fixes and preliminary source review. Independent medical/legal/security review still not performed.

No new product-level access rights, physician licensure, consent/health record, live membership/finance, identity or operational API. Protected data interlock remains **DENY-ONLY**. No Stage/QA/Release/Production or merge.
