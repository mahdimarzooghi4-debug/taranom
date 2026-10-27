# TRN-TECH-219 — Member appointment catalog selection

## Purpose
Replace free-form specialty and doctor references in the synthetic member request form with choices read from the selected center's existing PostgreSQL-backed journal.

## Read flow
- The member enters a synthetic `test:` center reference and explicitly requests its current directory.
- The server returns the current center snapshot, including registered department references and its source revision.
- The member selects a department and explicitly reads the doctor roster at that same version using TRN-TECH-102.
- The UI accepts only a well-formed synthetic roster whose center, department, revision, assignment state and deny-only authority flags all match.
- The patient chooses a doctor from the returned list. No automatic choice or free-form doctor override is permitted.
- The appointment request persists the selected center, department, doctor, and synthetic slot intent through TRN-TECH-216. If the center changes after the directory read, the version-pinned roster read fails closed and requires reloading.

## Boundaries
The current endpoint has no public center discovery/search catalog, so a synthetic center reference must be supplied. The slot remains a synthetic requested reference because no verified availability feed exists. Listing a doctor does not authenticate the center, prove licensure, verify appointment eligibility, or grant clinical access. No real provider or patient data, booking confirmation, payment, prescription, external insurer/identity API, or production route is introduced.

## Verification
The Chromium test seeds a synthetic center, department, and unverified doctor through the existing center journal. It verifies patient-side department/doctor selection, an intentionally lost response with byte-identical manual retry, and appearance of exactly one request in the matching center worklist. All actor, licensure, availability, booking, clinical and national-release flags remain false.
