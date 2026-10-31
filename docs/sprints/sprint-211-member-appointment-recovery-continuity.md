# Sprint 211 — Member appointment recovery continuity

## Goal

Keep an uncertain synthetic appointment request recoverable across a page reload in the same browser tab.

## Delivered

- Persist the exact synthetic request reference and fixed request body in a versioned session-scoped envelope before POST.
- Validate the stored record strictly on reload; recovered attempts stay unresolved until the server readback verifies the exact request.
- Clear the envelope after a valid receipt or matching readback; do not send when browser storage cannot preserve recovery state.
- Add a real Chromium regression that loses the acknowledgement, reloads, reads back the same request, and verifies one center queue entry and one POST.

## Boundaries

No automatic retry, real identity or member ownership claim, cross-device history, booking, availability, licensure, clinical action, Stage, formal QA, Release, Production, or merge.
