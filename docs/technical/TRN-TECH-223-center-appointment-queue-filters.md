# TRN-TECH-223 — Center appointment request queue filters

## Purpose

Allow the synthetic center worklist to narrow its read-only request page by a requested department, a patient-selected doctor, or both.

## Contract

The existing CI-only center queue GET accepts optional `departmentRef` and `requestedDoctorRef` synthetic-reference filters. They are conjunctive: a row is returned only if its verified latest request matches the selected center and every supplied filter. Stable request-reference ordering and the existing cursor/limit contract remain in force. The response echoes normalized filter values, and the browser rejects a response whose filters differ from those it requested.

Every candidate still has its event and audit lineage rebuilt from the isolated PostgreSQL journal before display. Invalid filter references or query fields fail closed. Empty filters preserve the existing unfiltered queue. Pagination carries the same filters forward.

## Boundaries

This is a read-only CI synthetic worklist. It does not define a request status, authenticate the center or doctor, establish licensure or slot availability, accept or reject an appointment, reserve a slot, or perform clinical work. All deny-only flags remain false.

## Validation

The full-stack Chromium test writes eleven matching rows and two non-matching rows for the same center, verifies the first ten and the next one preserve the filter, and asserts invalid filters are rejected.
