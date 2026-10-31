# TRN-TECH-221 — Member appointment recovery continuity

## Purpose

Preserve one uncertain synthetic appointment request across a page reload in the same browser tab, so the member demo can read back the same request reference before any manual retry.

## Browser storage contract

The demo writes a versioned envelope to `sessionStorage` before sending the first POST. It contains only the exact `test:` request references and fixed non-clinical request body required for reconciliation. No diagnosis, prescription, free-form note, or real identity is accepted. The record is scoped to the current tab session and has no client-side expiry, avoiding a new request merely because a recovery window elapsed.

On reload, the client validates the envelope version, request reference, UUID, exact field set, and synthetic reference format. Invalid data is ignored. A restored attempt starts unresolved: the client must read the same server request reference and compare the complete command and deny-only flags. It never automatically resends a POST.

The exact stored envelope is removed after a valid receipt or exact readback confirmation. If browser storage is unavailable, the UI does not send the initial request because it could not preserve the recovery information.

## Boundaries

This is browser continuity for CI-only synthetic QA, not member authentication, cross-device history, a production persistence contract, booking, availability, clinical action, or authorization. The isolated PostgreSQL journal remains the source read during reconciliation. All medical, booking, and national-release authority flags remain false.

## Validation

The Playwright regression loses the POST acknowledgement after the isolated journal records the request, reloads the page, verifies the restored request requires readback, and confirms the exact request appears once in the center queue without a second POST.
