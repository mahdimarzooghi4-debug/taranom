# Taranom Clinical Center Web — Sprint 05 disconnected UI

**Non-deployable, contract-only center-management shell.** One center, multiple departments; NO collaborating-network role or parent portal. Decisions TRN-BIZ-053/054 are currently tracked in [Draft PR #10](https://github.com/mahdimarzooghi4-debug/taranom/pull/10). The isolated .NET center governance reducer is in Draft PR #11.

Five routes within **one center panel**: `/center`, `/center/departments`, `/center/doctors`, `/center/qualifications`, `/center/audit`.

- This shell uses React + TypeScript + Vite, Persian RTL, keyboard/accessible browser routing, responsive sidebar and a non-mutating workflow-information dialog.
- The UI **never** authenticates a center, fetches actual doctors/departments, shows patient data, grants licenses or writes real/placeholder doctors. Action controls open information-only locked dialogs; their submit buttons are disabled. No browser local/session storage, fake active-doctor profiles, REST URLs, external CDN/fonts, or live authorization fallback is present.
- Empty/unavailable lists **must not** mean a real center has zero doctors or departments.
- The UI is separate from `apps/member-web` and is not connected to the pure `.NET` governance code. Exact real adapter/user/tenant/qualification/licensing contracts are pending. Do not expose this shell as a live medical administration console.

Local checks: `npm ci --ignore-scripts`, `npm run typecheck`, `npm run build`, `npx playwright install chromium`, `npm run test:e2e`. CI uses the committed version-pinned lockfile and tests the built preview, not Vite development mode.

This source is strictly internal Code/Code Review and not Stage, formal QA, Release or Production; it must remain a Draft PR until independently authorized.

## Sprint 25 — disconnected browser egress defense

The Center Web built preview uses the same restrictive HTML meta CSP and no-referrer/noindex metadata as Member Web. Three built-browser tests exercise policy presence, blocked local `/api/` + off-site `fetch`, and blocked inline script insertion. Existing disabled/nonmutating center actions and the server DENY-ONLY protected-data gate stay unchanged. Meta CSP is defense in depth for an **offline disconnected preview**, not center-management authorization, professional licensing, a legal medical data right or real hosting/anti-clickjacking policy. Future Production hosting would need approved HTTP headers and independent identity/consent/rights controls; HMR may be restricted.
