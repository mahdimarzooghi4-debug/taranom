# TRN-TECH-217 — Read-only center appointment request queue

## Purpose

Expose synthetic request intents already recorded by TRN-TECH-216 as a bounded, read-only center queue. The center receives only request, department, requested-doctor, and slot references, plus the journal revision. Member references and clinical details are not projected.

## Source and integrity

The queue reads through the appointment journal's separate restricted reader principal. It scans a bounded, ordered window of request references, rebuilds each candidate from its append-only event and audit records, and verifies revision continuity and event digests before including it. An incomplete scan or unverifiable history fails closed. Pagination uses request-reference cursors and limits each page to ten entries.

The API and browser route exist only under `TARANOM_APPOINTMENT_REQUEST_QA=CI_EPHEMERAL_ONLY`. The browser surface is built only for ephemeral CI with `VITE_TARANOM_APPOINTMENT_REQUEST_QA=1`.

## Workflow boundary

The center screen is read-only. Each entry is labeled as an unconfirmed request intent. The API does not accept, reject, reschedule, reserve, or confirm a request. Member authentication, center authentication, provider licensing, slot availability, booking confirmation, clinical action, and national release remain false.

The patient-selected doctor is preserved and displayed as the patient's requested provider; the assistant or center queue does not choose another provider. This is test UI, not an operational center portal.

## Validation

`apps/center-demo-web/tests/appointment-request-queue.spec.ts` creates isolated synthetic requests for two centers, reads the actual HTTP/PostgreSQL queue in Chromium, verifies center scoping, checks the requested doctor is preserved, and asserts the booking and authority flags remain false.
