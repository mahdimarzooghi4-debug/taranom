# TRN-TECH-034 — Browser egress fence for disconnected member/center previews

**Date:** 2026-10-10. Base: Sprint24, Draft PR #31. Governance: TRN-BIZ-035 / TRN-BIZ-053 / TRN-BIZ-054; identities, legal rights, provider professional qualification and privacy consent remain unverified.

## Gap and accepted technical boundary

Both Persian RTL React+Vite browser shells are intentionally unconnected; however, the static HTML had no browser-enforced resource/network policy. That leaves room for accidental outbound browser traffic or untrusted inline script if future UI code regresses. Add **only preview-scoped defense in depth**, not an authentication, permission, API, patient-data or Production change.

1. Include the SAME `meta http-equiv="Content-Security-Policy"` in each app's static HTML: `default-src 'none'; base-uri 'none'; object-src 'none'; frame-src 'none'; form-action 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'none'; worker-src 'none'; media-src 'none'`. This permits the built local JS/CSS but denies `fetch`/XHR/WebSocket including same-origin APIs, external resources, injected inline JS, and form submission. No external URL allowlist is invented.
2. Add `referrer=no-referrer`, and `robots=noindex,nofollow,noarchive` to both HTML preview documents. These are browser/crawler hints, not legal confidentiality guarantees.
3. Add THREE real Chromium Playwright cases per app against the built Vite artifact (not dev HMR): static CSP/meta and visible app; attempted same-origin `/api/blocked-csp-local` and off-site fetch both reject and emit **no matching HTTP request**; DOM-inserted inline script cannot execute. Keep existing sensitive-value/credential/request regressions.
4. Maintain the inherited TWELVE independent .NET, PostgreSQL, Member/Center Chromium, and Sprint08 DENY-ONLY CI jobs; document and repair red CI, capture exact-head evidence. No UI flows may gain a real login, wallet/medical/provider source or write action as part of this change.

## Limitations

**Meta CSP is not a response header** and cannot provide `frame-ancestors`, server-side anti-clickjacking headers, Content-Security-Policy-Report-Only, server-enforced authorization or a complete cross-origin privacy proof. A compromised same-origin asset could still execute under `script-src 'self'`. `connect-src 'none'` may prevent Vite dev HMR, so CI must use the production-built preview; it is NOT a real Production deployment. No external IdP, issuer, reviewer authority, professional license, healthcare consent, banking/insurance/payment or verified medical record exists.

No Ready for Review, Merge, Stage, QA Gate, Release or Production without explicit user authorization and independent security/legal/medical review.
