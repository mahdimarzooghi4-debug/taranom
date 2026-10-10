# TRN-SP-35 — Built-preview HTTP response security baseline

**Date:** 2026-10-10. Stacked on Sprint34 Draft PR #41, branch `sprint-34-external-ai-runtime-policy-gate`.

Business TRN-BIZ-035/036/037 privacy/internal security rules → TRN-TECH-044 → TRN-EP-12 Operational Readiness / TRN-EP-13 Member Web / Center Preview → Sprint35.

- **BL-350**: Introduce shared Vite **preview-only HTTP security headers** for two disconnected test shells, with enforceable HTTP `frame-ancestors 'none'` plus XFO DENY, CSP deny API egress, nosniff/referrer/permissions and no-store. No Production host chosen.
- **BL-351**: Attach identical shared header policy to both Vite configs without relaxing original HTML meta CSP, changing built frontends, adding a private API, or making mock data appear real.
- **BL-352**: Add **six** actual Chromium Playwright tests (three Member + three Center) proving deep-route real HTTP headers, hashed JS asset headers and active browser iframe denial from an unprotected parent. Preserve existing 54 browser cases.
- **BL-353**: Extend inherited **12-job** CI for this Sprint with exact-head Push and PR runs, preserving real PostgreSQL, protected data DENY-ONLY, source policy gate for external AI, .NET contracts and both browser applications.
- **BL-354**: Preliminary security Code Review COMMENT and CI evidence on exact final SHA, explicit warning Vite preview headers do not establish Production CDN/ingress policies; independent security/clinical/legal/financial review outstanding.

No real identity, member/patient record, health authorization, insurer, finance, AI inference, medical workflow, hosting provider selection, TLS certificate, Stage, QA Gate, Release or Production. All stacked PRs stay Draft/Open/Unmerged.
