# TRN-TECH-216 — Synthetic appointment request persistence

## Purpose

Persist the member's synthetic request intent across HTTP reads using the isolated PostgreSQL database used by the center demo regression. The request retains the doctor selected by the member and a synthetic slot reference.

## Runtime boundary

The routes are registered only when `TARANOM_APPOINTMENT_REQUEST_QA=CI_EPHEMERAL_ONLY`. They are hosted by the loopback-only center demo, require the isolated `taranom_sprint64_center` database, and use separate restricted writer and reader PostgreSQL principals. Event and audit rows are append-only; every read rebuilds the revision history and verifies each event digest and its audit correspondence.

The payload accepts only bounded `test:` references and a fixed set of non-clinical fields. Diagnosis, prescription, and arbitrary extension fields are rejected. The endpoint does not authenticate a member or center, check a doctor's license, verify slot availability, reserve or confirm an appointment, or authorize clinical action. All such flags, including national release, remain false.

This is an internal CI integration surface, not a production API or a live booking capability. It does not connect the member, center, insurer, or support applications.

## Validation

`tests/center_demo/test_appointment_request_http.py` exercises the real local HTTP host and PostgreSQL journal for persistence, exact idempotent replay, conflicting replay, stale revision, invalid references, origin rejection, unknown field rejection, doctor-reference preservation, and false authority flags.

The CI workflow invokes this test with the appointment QA feature flag enabled only for its ephemeral PostgreSQL-backed server.
