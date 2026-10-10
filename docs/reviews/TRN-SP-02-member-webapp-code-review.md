# TRN-SP-02 — Member Web App source-level Code Review and executable evidence

**Date:** 2026-10-10  
**PR:** [#5](https://github.com/mahdimarzooghi4-debug/taranom/pull/5) — **Draft/Open, unmerged**.  
**Reviewed code SHA:** `6031c928d4b3777d25f29d8ea81564db240ac088`.  
**Reviewer status:** **Implementation-assistant source-level review, NOT an independent human reviewer approval.** GitHub PR #5 had 0 submitted review(s) at review-record creation.  
**Authorization:** React/TypeScript member Web App **disconnected shell only**, Persian RTL, navigation, accessibility, safe unavailable states and real Chromium tests. No IdP/BFF, wallet, insurance, clinical/provider data, finance/payments, AI, Stage or Production.

## Findings and corrections

| ID | Severity before fix | Finding | Code fix | Concrete validation |
|---|---|---|---|---|
| WEB-CR-01 | HIGH / test assurance | Chromium tests ran Vite's **development/HMR server** instead of the production build, so green E2E did not prove that the shipped static bundle worked. | Playwright `webServer` now starts `vite preview` on 127.0.0.1:4174 **after** CI builds `dist/`. Added the missing `preview` package script after the first exact-head failure. Browser test requires `/assets/index-*.js`, rejects dev `/@vite` and `/src/` scripts. | 16 Playwright Chromium cases passed against built artifact on exact SHA; [PR run](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/38037177189) and [push run](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/38037174244). |
| WEB-CR-02 | MEDIUM / accessibility | SPA navigation focused the pre-update `main` element and browser Back/Forward did not explicitly transfer focus to the newly rendered heading; this can disorient keyboard/screen-reader users. | After route state render, focus the new `h1` programmatically, including history `popstate` and safe unknown-path recovery, without stealing focus on initial direct load. | A named real-browser test covers link navigation, Back, Forward, and not-found recovery, asserting focused headings. |
| WEB-CR-03 | MEDIUM / accessibility | Collapsible mobile navigation had no Escape-to-close/focus-return behavior. | `Escape` closes the expanded menu and returns focus to the menu toggle; event listeners are scoped to the open state and cleaned up. | Real mobile-viewport keyboard test asserts `aria-expanded=false`, focus return and hidden navigation. |
| WEB-CR-04 | LOW / CI wiring | First attempt at production-bundle testing lacked the required `npm run preview` script; both first CI runs failed at Playwright's web-server startup. | Added `preview: vite preview` to `package.json`; no dependency versions changed, so lockfile dependency graph remains valid. | Failure observed in exact-SHA logs for `d8bbc3086f56c629a79a192ba96c465351b42b6e`, then fixed and 2/2 CI success on `6031c928d4b3777d25f29d8ea81564db240ac088`. |

## Verified code and release-boundary properties (within authorized shell)

- Exactly nine member-facing browser paths: overview, membership, medical credit, consent/privacy, care, records, nonbinding cost awareness, billing, support. Unknown paths show a safe 404 without a member record.
- All screens remain explicitly disconnected; no authenticated user, session cookie/token, name, personal wallet amount, insurance decision, medical history, real provider/payment, AI inference, or `/api/v1` calls are introduced. Query strings `?demo=true&authenticated=true` cannot grant access.
- The browser is tested for empty `localStorage`/`sessionStorage` after navigation and for absence of private-API and off-site requests; remote Google Fonts previously removed.
- User-facing copy keeps nonbinding cost estimates separate from issued invoices, care navigation separate from real clinical decisions, and the emergency warning independent of any financial/admin precondition.
- RTL `lang=fa`/`dir=rtl`, skip-link focus, mobile menu state and Back/Forward focus transitions are tested. No automated WCAG-2.2-AA certification, screen-reader user study, color-contrast audit or assistive technology testing has been performed.
- The React/TS build uses pinned direct dependencies plus committed `package-lock.json` with CI `npm ci`; CI actions are commit-SHA pinned. Node's exact patch, browser operating-system image, dependency vulnerability assessment and artifact attestation are not yet fully approved.

## Exact-head evidence

- `PR 38037177189` — **COMPLETED / SUCCESS**: https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/38037177189
- `Push 38037174244` — **COMPLETED / SUCCESS**: https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/38037174244
- [Playwright XML and traces artifact](https://github.com/mahdimarzooghi4-debug/taranom/actions/runs/38037177189): **16 passed, zero failed**, Chromium on Vite `dist` preview; TypeScript/production build and static forbidden-adapter checks also passed.
- Earlier test-wiring failure is retained in run history; it was not a clinical/business policy flaw, and it was not masked by skipping the tests.

## Open gates — not to be implied by green CI

1. **Independent Code Review approval is missing.** Assistant review and passing CI are evidence, not a separate accountable reviewer.
2. **WEB-02/WEB-03 authoritative identity, member/tenant isolation** cannot be evaluated until reviewed .NET owner/IdP/BFF contracts exist; no fake identity or source-of-truth data can substitute.
3. **Member interface is not a completed health platform**: real session, privacy/medical consent, member finance, provider/insurer records and real source integrations all remain intentionally disconnected.
4. Broader accessibility validation (contrast, automated accessibility scans, real screen readers, assistive-technology UX), cross-browser coverage beyond Chromium, end-to-end authorization, dependency vulnerability review, artifact attestation, CSP/hosting headers and production security controls need separately defined gates.
5. Business/Technical/Sprint 01/planning Sprint 02 PRs #1–#4 and code PR #5 **remain Draft/Open and unmerged**. No Stage, QA Gate, Release Approval or Production has been executed.

**Review outcome:** **Source review and remediation for WEB-CR-01…04 complete with green CI; independent Code Review PENDING. No Merge/Stage/QA/Release/Production.**
