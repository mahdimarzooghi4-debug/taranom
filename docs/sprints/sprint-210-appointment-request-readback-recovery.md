# Sprint 210 — Readback before retry

## Goal
Make uncertain member appointment writes recover through exact journal readback before another POST can occur.

## Delivered
- Retain the original request reference and command after an uncertain response.
- Reconcile against the existing read endpoint and validate the full command and deny-only flags.
- Enable a manual exact retry only after the journal proves the request is absent.
- Add browser coverage for successful-write/lost-ack and no-write/lost-ack scenarios, including center queue verification.

## Boundaries
No automatic retry, real identity or member ownership claim, booking, availability, licensure, clinical action, Stage, formal QA, Release, Production, or merge.
