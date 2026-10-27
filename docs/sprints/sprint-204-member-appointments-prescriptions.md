# Sprint 204 — Member Appointments and Prescriptions

## Goal

Add the owner-confirmed member routes for appointment scheduling and Taranom-held prescription records, together with the center schedule entry point.

## Code

- Added member navigation and pages for appointment scheduling and prescriptions.
- Added a center route for schedule and appointment requests.
- Kept all controls unavailable until real identity, center schedule, provider qualification, and prescription record sources are connected.
- Added browser checks that pages do not invent appointments, medication, doctors, or availability and that the member remains the final doctor chooser.

## Validation

The full repository CI on the exact PR head must pass, including the member and center built-bundle browser suites.

## Boundaries

This sprint adds product routes, not live appointment booking or prescribing. No insurer panel/API integration, patient account, medical record, prescription, provider, appointment, financial transaction, or production permission is fabricated. The PR remains Draft/Open/Unmerged.
