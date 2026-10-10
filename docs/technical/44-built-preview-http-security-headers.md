# TRN-TECH-044 — Real HTTP security headers for disconnected built browser previews

**Date:** 2026-10-10. **Base:** Sprint34 Draft PR #41. Scope: two nonoperational Persian RTL shells `apps/member-web` and `apps/center-web`. Under TRN-BIZ-035/036 privacy, TRN-BIZ-037 internal-only AI and the already accepted deny-only principle for protected clinical information, a browser-delivered preview MUST not permit active embedded controls, API egress, external tracking or clickjacking through an unauthorized iframe.

## Problem

Sprint25 introduced **HTML meta** CSP in both built previews (`default-src 'none'`, `connect-src 'none'`, `script-src 'self'`, `form-action 'none'`) with Chromium runtime checks. These meta tags cannot enforce **`frame-ancestors`**, and cannot by themselves establish the HTTP response policy of a hosting server. There has been **no Production hosting/provider selection**. This sprint advances a verifiable delivery security boundary for the **existing localhost Vite built-preview HTTP server only** without pretending it configures a future static host or real member/provider portal.

## Shared, machine-tested server-side HTTP policy

Create `tools/preview_security/headers.ts`, imported by BOTH Vite configs and attached **only to `preview.headers`**. This retains existing Vite dev behavior and avoids introducing a dev HMR transport exception into the disconnect-only built preview. The resulting localhost HTTP responses, including deep SPA routes and built `/assets/` files, use one canonical shared policy:

- `Content-Security-Policy`: `default-src 'none'`, `base-uri 'none'`, `object-src 'none'`, **`frame-ancestors 'none'`**, `frame-src 'none'`, `form-action 'none'`, `script-src 'self'`, `style-src 'self'`, `img-src 'self' data:`, `font-src 'self'`, `connect-src 'none'`, `worker-src 'none'`, `media-src 'none'`.
- `X-Frame-Options: DENY` as older-browser defense; `X-Content-Type-Options: nosniff`; `Referrer-Policy: no-referrer`.
- `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=(), usb=()`. These are not approvals or capabilities to access medical sensors or payment services.
- `Cross-Origin-Opener-Policy: same-origin`; `Cross-Origin-Resource-Policy: same-origin`, and `Cache-Control: no-store` for this disconnected preview, not a final production caching design.

Keep the prior HTML meta CSP as a defense in downloaded HTML, but the actual `frame-ancestors` policy is **HTTP-only**. The unchanged browser bundles may use only locally bundled scripts/styles/fonts plus image `data:`; there is no real API, consent/identity session, provider integration, wallet or clinical data. No local or external AI request.

## Actual browser and response evidence

Add three real Playwright Chromium tests **to each app**, exercising its **built Vite preview**:

1. Deep-route HTTP response is HTML with CSP including header-only `frame-ancestors 'none'` and all named network/identity/privacy restrictions; existing visible app route still renders, ensuring nonblocking functionality.
2. Hashed production JavaScript asset HTTP response includes nosniff, no-store and CSP.
3. Chromium top-level `about:blank` embeds the preview URL in an iframe (the parent has NO CSP). Await the iframe load event, then verify the blocked child exposes neither React `#root` nor `main`. Chromium console error wording is not an acceptance condition; HTTP policy headers are independently asserted. This tests the **real server response's anti-embedding enforcement**, not a meta-tag string or a parent's `frame-src` policy.

Existing 33 Member + 21 Center Chromium cases still exercise disconnected UI, navigation/RTL/accessibility, safety notices, CSP denial of private same-origin and external fetch, inline-script blocking and no browser storage. Six new tests should yield **36 Member and 24 Center**, subject to actual CI logs. Inherit **all 12 CI jobs**, including real PostgreSQL and clinical protected-data DENY-ONLY, plus Sprint34 static external AI source gate.

## Hard boundaries / actual Production work remains

`vite preview` serves built assets on loopback in CI, **not the chosen Production host**. `preview.headers` is not packaged into `dist/` static files or automatically transported to CDN, nginx, object storage, container ingress or reverse proxy. Production headers, TLS, authentication/session protections, real PHR/consent or clinic access, host-level cache/security/reverse proxy and ingress/network egress control need explicit actual-infrastructure decisions and **independent security review**. No HSTS header is invented for plaintext localhost; HTTPS/HSTS requires a deployed HTTPS contract. CSP and XFO also do not establish genuine user authorization, legal consent or licensed healthcare.

A security control passing CI is a technical observation ONLY, never Stage admission, actual Production security approval or launch readiness. All PRs remain Draft/Open/Unmerged; no Ready for Review, Merge, Stage, QA Gate, Release or Production without explicit user approval.
