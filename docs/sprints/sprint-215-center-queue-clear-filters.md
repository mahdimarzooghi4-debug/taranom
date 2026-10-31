# Sprint 215 — Center Queue Filter Reset

## Goal

Let center staff clear optional department and patient-selected-doctor filters and return to the first page of the synthetic queue.

## Delivered

- Add a reset control that clears both optional fields and reloads the unfiltered first page.
- Add Chromium coverage for filtered-to-unfiltered transition using distinct synthetic requests.
- Record the behavior contract in TRN-TECH-225.

## Safety boundary

Only synthetic CI data is used. The queue remains read-only. No identity, provider qualification, capacity, booking, acceptance policy, clinical action, Stage, formal QA, Release, Production, or nationwide-launch permission is added.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human code review remain separate gates.
