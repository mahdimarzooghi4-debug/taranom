# Sprint 214 — Appointment Queue Single-Filter Regression

## Goal

Verify that either optional center queue filter works independently, while preserving the existing combined-filter behavior.

## Delivered

- Added synthetic Playwright coverage for department-only and patient-selected-doctor-only queue reads.
- Compared result references and row values against three deliberately distinct test records.
- Documented the filter response contract in TRN-TECH-224.

## Safety boundary

Read-only synthetic CI data only. The queue does not accept, reject, reserve, or confirm appointments. It does not assert real identity, professional licensing, slot availability, or clinical authorization. No Stage, formal QA, Release, Production, or nationwide launch is authorized.

## Delivery

Keep this PR Draft/Open/Unmerged. Exact-head CI is required before considering this sprint complete. Human code review remains a separate gate.
