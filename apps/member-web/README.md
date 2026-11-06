# Taranom Member Web — Sprint 02 authorized code slice

**Scope:** Persian RTL accessible React/TypeScript **member app shell only**. No real user login, account, credit values, clinical records, insurer/provider integrations, payment, AI or synthetic fake real-world authority.

- `npm ci --no-audit --no-fund --ignore-scripts`
- `npm run typecheck`
- `npm run build`
- `npx playwright install chromium` then `npm run build` and `npm run test:e2e`

The `/member` app intentionally renders each member section in an explicitly unavailable state. Navigation paths are browser routes **only** and do not imply existing .NET read endpoints. It never sends private network API requests or persists sensitive state in browser storage. Pages and tests cover WEB-01, WEB-04…12 under this **shell-only** authorization. WEB-02/03 backend isolation requires later real backend identity/tenant contracts and must not be claimed satisfied by a disconnected shell.

The separate `/patient-portal` route is a Figma-matched, frontend-only presentation of the seven Patient Web Portal screens. Its named people, appointments, prescriptions, balances, and statuses are synthetic design examples. Actions are disabled; no real member record, insurance response, appointment, payment, or clinical authority is represented. This route is not connected to a backend and is not a replacement for the protected `/member` shell.

The separate `/patient-app` route is a mobile-first Figma preview covering the patient app's home, appointments and booking flow, provider profiles, records and prescriptions, support credit and insurer coverage views, smart guidance, referrals, consent, support, and membership/group journeys. It uses synthetic values and does not save or send data. Membership amounts remain illustrative because the age-based fee schedule is not approved. This preview also does not replace the protected `/member` shell.

Exact direct and transitive npm dependencies are committed in `package-lock.json` (generated and validated from successful GitHub CI). CI uses `npm ci --ignore-scripts`; changes to dependency versions require an updated reviewed lockfile. No Stage/QA Gate, Production, or member launch.

Mandatory product member Web App is TRN-EP-13. This code implements the layout and safe baseline only; no Backend BFF or Production member authentication exists.

Privacy hardening: no third-party remote font, analytics or content dependencies. The UI uses local/system font fallbacks so loading the unauthenticated shell sends no data to external domains.

Code Review update: Playwright runs the actual Vite production bundle using `vite preview`, not the development HMR server. Client routing focuses the new heading after link navigation and browser Back/Forward; the mobile menu closes with Escape and returns focus to its toggle. These are accessibility controls for the disconnected shell, not a real member authorization layer.

## Sprint 25 — disconnected browser egress defense

The built static member preview now includes HTML **meta Content-Security-Policy**: only self-hosted scripts/styles and local/data images; no `fetch`/XHR/WebSocket (`connect-src 'none'`), third-party assets, inline script, workers, frames, objects or form actions. It also requests `no-referrer` and `noindex,nofollow,noarchive`. Three real Chromium regressions check document policy, attempted same-origin private and remote fetch denial before actual requests, and blocking an injected inline script. This is intentionally for the built Vite preview; development HMR may be blocked. Meta CSP is NOT a deployment HTTP header, `frame-ancestors` cannot be enforced from meta CSP, and no real IdP, lawful member access or Production hosting exists.


#### Real appointment API slice (Draft)

The `/patient-app/appointments` and `/patient-app/request-tracking` routes now read the authenticated member's appointments from `GET /api/v1/me/appointments`. A patient can cancel their own booked appointment; cancellation remains in their history and is visible to the center. The UI requires a host-provided in-memory `window.taranomSession.getAccessToken()` bridge and a same-origin reverse proxy for `/api/v1`; it does not persist tokens. No browser OIDC login is configured here. Provider search, city/specialty discovery, new booking, prescriptions, records, payments, insurance and other patient routes remain disconnected previews.

## Current browser-install preparation

The integration branch includes `/patient-app/install`, linked from account, and
`/manifest.webmanifest`. An installed browser app launches the connected account
route. PNG icons are reproducible with `node scripts/render-app-icons.mjs` after
installing the existing Playwright Chromium runtime. There is no service worker
or offline account/API cache. Installation requires an appropriate HTTPS host
and browser support; no Android APK or signed native release is produced.
See `docs/technical/TRN-TECH-280-patient-browser-install.md` from the repository
root for the exact scope and verification limits. Protected services still need
the host's real bearer session and same-origin API routing.
