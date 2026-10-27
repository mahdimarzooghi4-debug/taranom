# Sprint 220 — Strict Schedule Timestamp Syntax

## Goal

Make schedule query timestamps unambiguous at the HTTP boundary, independent of machine locale and caller URL encoding.

## Delivered

- Enforce a strict offset-bearing timestamp grammar before parsing.
- Limit fractional precision to the supported .NET tick precision.
- Exercise encoded numeric offsets in the Kestrel integration test.
- Reject free-form dates, offset-free dates, impossible dates, and excessive fractional precision.

## Safety boundary

No timezone policy, center opening-hour rules, provider source, live schedule, booking, Stage, formal QA, Release, Production, or national launch is added or enabled.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate gates.
