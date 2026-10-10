# TRN-SP-25 — Disconnected member/center browser resource and egress guard

**Date:** 2026-10-10. Base `sprint-24-center-modal-keyboard-boundary` (Draft/Open PR #31). Business TRN-BIZ-035/053/054 → Technical TRN-TECH-034 → Scrum Backlog → Sprint25 → Code → Chromium+full CI → preliminary Code Review.

- **BL-250:** identical restrictive static meta CSP in Member and Center built previews; disallow client network access including same-origin private APIs while both apps are disconnected.
- **BL-251:** no-referrer and noindex/noarchive instructions only, with explicit limits; no real hosting or legal confidentiality assertion.
- **BL-252:** six Chromium regression cases (3 per app) for CSP, blocked external/local fetch and inline script denial, while preserving existing 18 Center and 16 Member browser checks.
- **BL-253:** preserve inherited 12 exact-SHA CI jobs including isolated PostgreSQL negative tests and clinical protected-data **DENY-ONLY**.
- **BL-254:** source-level COMMENT review, exact-head CI evidence and documentation of remaining external authority/identity/licensing/rights/HTTP hosting contracts. No independent medical/legal/security approval claimed.

No real member account, clinician, patient data, provider contract, payment/insurance, API, tokens, issuer or production-admission feature is authorized. Keep Draft/Open/Unmerged; no Stage/QA/Release/Production.
