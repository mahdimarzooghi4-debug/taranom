# TRN-TECH-218 — Synthetic member appointment request surface

## Purpose
Connect a member-facing, CI-only form to the existing synthetic appointment request journal and Sprint 207 center queue.

## Contract
- Expose `/member-demo/appointment-requests-qa` only while `TARANOM_APPOINTMENT_REQUEST_QA=CI_EPHEMERAL_ONLY` is set by the disposable regression workflow.
- Build the member browser route only with `VITE_TARANOM_APPOINTMENT_REQUEST_QA=1`.
- Accept only explicitly entered `test:` references for member, center, department, patient-selected doctor, and requested slot. The form collects no name, contact, clinical details, or real identity.
- Submit the exact TRN-TECH-216 command to the existing journal. The center's bounded TRN-TECH-217 queue can then display the same synthetic routing intent.
- On an uncertain network result, retain the exact request reference, request ID, and JSON body. Retry occurs only after a manual click and sends the identical command; the journal's idempotency contract handles replay.
- Verify the returned contract, single-command projection, selected doctor, revision, and all false authority flags before showing the receipt.

## Explicit limits
A recorded request is only intent. It does not authenticate the member or center, check doctor licensing or slot availability, confirm a booking, issue a prescription, perform a clinical action, permit AI training, or permit national/production release. The patient chooses the doctor. No external API or real data is used.

## Verification
The Chromium end-to-end test forces the first response to be lost after the synthetic write, confirms there is no automatic retry, manually retries the byte-identical JSON body, and checks that exactly one revision appears in the center queue with all authority flags false. CI also runs existing C# journal, HTTP, Python, policy, and browser regression suites.
