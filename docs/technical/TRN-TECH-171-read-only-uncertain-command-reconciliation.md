# TRN-TECH-171 — DRAFT / read-only reconciliation of uncertain synthetic commands

Date: 2026-10-18. Bounded internal technical contract. Business authority, identity, clinical, evidence rights, finance, external systems, Stage, and national release remain unchanged and denied.

## Problem and user outcome
In three existing CI-only member journeys — nonclinical coordination, unverified late-evidence review and synthetic proofing — the UI properly retains the original command JSON and UUID when the HTTP POST acknowledgement is lost or forged. Previously, resolving uncertainty required another POST (manual exact idempotent replay) even when the journal had already committed the original request. A safer and clearer user action is to **inspect the exact existing PostgreSQL journal event without performing any write**.

## Contract
- When a command is uncertain, show a separate explicit action: **بررسی فقط‌خواندنی فرمان قبلی**.
- This action makes exactly one ordinary same-origin, no-credentials, no-store GET against the existing single-case journal endpoint, not a POST and not an approval API.
- A successful read can clear uncertainty only when the existing TRN-TECH-170 command-to-event validator finds **one exact matching original request UUID at its expected event revision**, with the correct action and all existing scoping fields (for proofing, UUID, action, case/identity revision and review). In this mode the response must have outcome `READ`. All old synthetic denial flags must remain false. A later append to the same journal is allowed; reconciliation must not undo it.
- Missing, malformed, forged, stale, tampered, unavailable, or unbound reads keep the volatile original JSON/UUID, keep new commands disabled, and leave explicit manual same-UUID POST replay as the only existing write recovery. No new auto-retry, cancellation, status inference from absence, storage, background work, or server schema.
- After a verified read, show the current non-authorizing journal history and allow normal synthetic follow-up actions. All values remain **unverified test claims**, not real person/clinician or evidence verification.

## QA and gate
Six new Chromium end-to-end tests (three successful GET recovery with real PostgreSQL data and **zero additional POST**; three forged GET responses that must remain locked until safe manual replay), all existing replay/idempotency and source-integrity checks, and the unchanged shared 14-job regression. A passing exact-head CI and technical COMMENT are not independent human Code Review or authorization for Stage/Production. National launch remains `DENIED_BY_DEFAULT`.
