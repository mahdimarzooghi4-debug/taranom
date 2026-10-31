# Sprint 216 — Appointment Schedule Source Port

## Goal

Prepare the technical boundary for a future center schedule API without inventing a provider or sample availability.

## Delivered

- Add a provider-neutral .NET read port scoped by center and optional department and patient-selected doctor.
- Add a default-deny read service: no source means no slots.
- Validate source response shape, scope, slot uniqueness, ordering, and intervals.
- Keep every received source result unauthenticated and unverified; booking and clinical action remain false.
- Add unit coverage for missing source, valid unverified claims, bad scope/data, and source failure.

## Safety boundary

No center, insurer, or schedule API was supplied or connected. No credentials, real schedules, slot holds, bookings, prescriptions, clinical data, or production route are introduced. Real adapters remain blocked until source details and authorization are provided.

## Delivery gates

Keep this PR Draft/Open/Unmerged. CI and human code review are separate gates; no Stage, formal QA, Release, or Production.
