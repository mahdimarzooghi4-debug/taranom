# TRN-TECH-222 — Synthetic member appointment history

## Purpose

Provide a bounded read-only view of synthetic appointment request intents for the same synthetic member reference used when creating them.

## Contract

The CI-only GET route `/internal/demo/v1/members/{memberRef}/appointment-requests` scans a repeatable-read PostgreSQL snapshot, rebuilds and verifies each candidate's event and audit lineage, then returns at most ten matching requests in stable request-reference order. The opaque cursor is the last returned request reference. Unexpected query parameters, invalid synthetic references, incomplete scans, or unverifiable lineage fail closed.

The response reports contract `TRN-TECH-222`, `syntheticOnly=true`, and all authentication, licensure, availability, booking, clinical-action, and national-release flags as false. Returned rows omit the member reference and include only request, center, department, patient-selected doctor, and requested-slot identifiers.

## Limits

The route is enabled only by the CI ephemeral QA flag and loopback demo host. The supplied member reference is a filter, not authentication, ownership, consent, or access control. It must never be treated as a production member-history endpoint. This read surface does not accept, reject, schedule, or confirm a request and does not change patient choice of doctor.

## Validation

A real Chromium/PostgreSQL-backed HTTP regression creates eleven requests for one synthetic reference and one for another, checks stable ten-plus-one pagination, verifies the other reference is excluded, and asserts that identity and booking authority remain false.
