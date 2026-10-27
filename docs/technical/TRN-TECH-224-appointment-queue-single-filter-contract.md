# TRN-TECH-224 — Independent Appointment Queue Filters

## Purpose

Specify the bounded read-only queue behavior when a center narrows synthetic appointment requests by one optional filter at a time.

## Contract

- A caller may provide `departmentRef`, `requestedDoctorRef`, both, or neither.
- When one filter is supplied, every returned row must match that value.
- When both are supplied, a row must match both values.
- Filter values are returned with the page so the client can verify that the response corresponds to its request.
- Existing stable cursor pagination and request/audit lineage verification remain in force.

## Verification

The synthetic Playwright regression seeds three rows covering department-only, doctor-only, and combined matches. It checks the two single-filter result sets against their expected request references and row fields.

## Limits

This contract lists patient-selected intent in synthetic QA data. It does not verify a real patient's identity, a center's authority, a doctor's license, slot availability, acceptance policy, or booking. It grants no clinical, training, Stage, Release, Production, or national-launch authority.
