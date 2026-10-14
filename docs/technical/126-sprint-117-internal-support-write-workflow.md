# TRN-TECH-126 — Sprint 117 existing support command vertical flow

Status: isolated development/CI only; no external API. Reuses the existing `POST /internal/demo/v1/support/{caseRef}/commands` TRN-TECH-075 contract, canonical `SyntheticSupportIntake.Apply`, and append-only PostgreSQL journal; do not create any production route, provider integration or real authorization.

## Goal / acceptance
- After a verified test-only support snapshot, React may send a first `SUBMIT_UNVERIFIED` command at revision zero, or a `FOLLOW_UP_UNVERIFIED` command bound to the previously accepted original submission, and subsequently refetch the persisted snapshot and history.
- Commands use fresh UUID request IDs, current expected revision, `test:` references, and canonical exact ten-field body. HTTP errors and unknown/unauthorized response schema fail closed; never display a success based only on sending a request.
- A failed/uncertain attempt retains its request UUID for exact replay without manufacturing an extra event. Changing editable data resets that UUID. Database enforces revision concurrency, replay and scope checks.
- Test actual Chromium → localhost-only .NET → isolated PostgreSQL for first submission and follow-up, invalid refs and rejected origin; run full 14-job CI and preliminary Code Review.

## Non-authorities
These are synthetically claimed actors only, with no verified identity or lawful access. No medical decisions, actual complaints, insurers, payment, real member data, launch or operational authentication. TRN-TECH-012 and Issue #126 remain blocked pending externally authoritative evidence. National release `DENIED_BY_DEFAULT`. No Stage/QA Gate/Production or merge.
