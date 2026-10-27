# Sprint 223 — Strict Schedule Provenance Timestamp Syntax

## Goal

Make timestamps captured with an appointment request follow the same exact syntax as timestamps accepted by the schedule-read boundary.

## Delivered

- Extracted the bounded timestamp parser into a shared source file.
- Applied it to the observation timestamp, selected slot start, and selected slot end before command deserialization.
- Added HTTP integration cases for missing offsets, invalid separators, impossible dates, and excessive fractional precision.
- Updated the synthetic appointment-request response contract to TRN-TECH-233.

## Safety boundary

This validates syntax only. The values remain unverified claims; no source identity, freshness, availability, booking, clinical action, or production authority is added. The member demo sends null without a connected schedule source.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
