# Sprint 208 — Member synthetic appointment request

## Goal
Complete a synthetic end-to-end appointment request path from the member surface to the center's read-only worklist.

## Delivered
- Added an opt-in member request page for synthetic member, center, department, doctor, and slot references.
- Preserved the patient's doctor choice and showed a receipt only after validating the journal response and all deny-by-default flags.
- Added manual exact-payload retry for uncertain responses, relying on journal idempotency without sending automatic retries.
- Gated the server route and browser link to the disposable CI-only appointment QA configuration.
- Added a Chromium scenario covering lost response, manual replay, and center queue visibility.

## Boundaries
This records request intent only; it does not authenticate actors, check doctor credentials or availability, confirm appointments, create prescriptions, perform clinical action, authorize model training, or authorize production/national release. No stage, formal QA, release, production, or merge is included.
