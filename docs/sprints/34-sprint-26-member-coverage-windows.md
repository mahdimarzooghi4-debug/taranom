# TRN-SP-26 — Historical membership active/lapse/reactivation time windows

**Date:** 2026-10-10. **Stacked base:** `sprint-25-disconnected-web-egress-csp`, Draft PR #32.

Business TRN-BIZ-012/013/014/015/016 → Technical TRN-TECH-035 → Backlog → Sprint26 → pure .NET code → unit tests → full 12-job CI → preliminary source Code Review.

- BL-260: create a separate internal synthetic-only, **pure historical** member coverage interval inspector. Require input timeline with exact person, unique event UUID, sequential revisions and strictly increasing timestamps; never use payment/registration as activation.
- BL-261: distinguish only fully closed recorded active periods, fully closed nonrenewal gaps, mixed intervals and insufficient/malformed evidence; never infer open-ended current status.
- BL-262: write xUnit coverage for initial activation, reactivation, no credit during nonrenewal gaps, late inquiry into previous active coverage, exclusive temporal boundaries, offset-equivalent instants, subject isolation, malformed revisions/event identities/order and missing/evolving history.
- BL-263: enforce **no amount/credit/wallet posting, financial entitlement, AI, authorization, API or Production integration**; do not touch accepted 90-day waiting rule or protected clinical-data DENY-ONLY.
- BL-264: run exact-head prior 12-job CI (pure Member tests, both Chromium suites, real synthetic PostgreSQL and protected-data negative tests), independently inspect code, record evidence. Independent legal, financial, medical and security validation still outstanding.

This is not real-source membership evidence. All user-supplied markers are `test:` synthetic data and all statuses are mere temporal diagnostics. No claim of real late financial benefit or automatic allocation. Draft/Open/Unmerged only.
