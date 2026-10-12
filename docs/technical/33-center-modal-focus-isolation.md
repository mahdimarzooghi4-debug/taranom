# TRN-TECH-033 — Center disconnected modal keyboard isolation

**2026-10-10.** Test-only, disconnected Center Web foundation. Derived from approved TRN-BIZ-053/054 and existing single-center, no-permission preview. Base Sprint23 Draft PR #30. This is a **browser UI safety and accessibility** contract, not medical, contractual, identity or finance authorization.

## Observed gap

The existing Center Web marks a locked action panel `role="dialog"` and `aria-modal="true"` and moves initial focus to its enabled dismiss button. However, it does not trap Tab/Shift+Tab and does not make the rest of the page inert while open. A keyboard-only user could move focus behind the visual modal into unsupported navigation; this contradicts the declared modal contract even though all substantive operations are still disabled.

## Implementation contract

1. Keep all four operation dialogs (`department`, `doctor`, `assignment`, `end-affiliation`) explanatory and **nonmutating**. No added field, permission, success label, login, or real center/doctor record. Disabled save stays disabled.
2. While the modal is open, set HTML `inert` on the sidebar and workspace. Exclude the standalone skip link from tab order and accessibility tree until close. The overlay/dialog remains outside inert regions and has proper name/description.
3. Reuse the existing single enabled dialog close button as the keyboard focus target. Trap Tab **and** Shift+Tab to that control; Escape closes and returns focus to the original action trigger. Other keyboard navigation must not bypass the modal or activate a background route. This is an accessible focus-boundary behavior, never a browser authorization boundary.
4. Opening an action closes the mobile menu. Browser Back/Forward routes dismiss an open stale modal, restore non-inert content and route focus; the browser URL must not be used as an authenticated center scope.
5. Retain the current disconnected Persian RTL Center Web and Member Web with no external calls, private API, storage mutation, actual approval or financial/clinical record. All test-only `test:` PostgreSQL and Sprint08 **DENY-ONLY** gates are untouched.
6. Add real built-Vite Chromium Playwright tests for forward/backward focus containment, all four dialogs' Escape/dismiss focus restoration, navigation/history dismissal, mobile menu coexistence, disabled save/no private API. Run exact-head inherited 12-job CI including real PostgreSQL.

## Boundaries

HTML `inert`, dialog attributes and JS focus management are UX safeguards, **not** authentication, legal patient privacy consent, verified clinician licensing, professional appointment, or authority to perform clinical operations. No real IdP, issuer, revocation source or independent security/legal/clinical approval exists. Keep PR Draft/Open/Unmerged; no Ready/Merge/Stage/QA Gate/Release/Production without explicit permission.
