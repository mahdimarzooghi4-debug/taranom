# Sprint 205 — Synthetic Appointment Request Domain

## Goal

Begin the backend code for Taranom's core appointment service with a versioned request-intent contract.

## Code

- Add a test-reference-only appointment request command and immutable snapshot.
- Preserve the doctor reference supplied by the member; no assistant or reducer chooses a doctor.
- Reject stale revisions, invalid scopes, exact-request conflicts, and non-test identities.
- Project all identity, provider-license, slot-availability, booking-confirmation, and clinical-action authority as false.
- Add unit tests for the allowed and denied outcomes.

## Validation

The exact PR-head GitHub CI must run the member governance test suite and complete the repository's 14-job regression workflow.

## Boundaries

This is backend domain code, not a real appointment API. No actual availability, booking, patient data, provider, clinical service, or production permission is enabled. All changes remain in a Draft PR.
