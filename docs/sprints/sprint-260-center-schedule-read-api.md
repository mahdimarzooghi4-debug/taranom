# Sprint 260 — Center schedule read API

## Goal

Complete the center's appointment schedule read/write loop so its portal can retrieve the slots it manages through the protected API.

## Delivery

- Add `GET /api/v1/centers/{centerRef}/appointment-slots` under the existing center appointment capability and exact central grant scope.
- Return every slot for that center, ordered by start time, together with the current schedule revision; an empty center schedule returns an empty list and revision zero.
- Extend HTTP integration coverage to create a slot and read that same slot through the center API.

## Limits and remaining

- This endpoint only exposes the center's own configured schedule. It does not add the city/specialty/provider directory needed for patient discovery.
- Center identity enrollment, verified OIDC, grant administration, BFF/session and browser API clients remain outstanding.
- Draft/Open only. No merge, Stage, formal QA, release or Production deployment.
