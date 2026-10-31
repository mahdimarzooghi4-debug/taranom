# Sprint 246 — Canonical panel claim encoding

## Goal

Require one canonical representation for panel grant claim values and reject oversized claim payloads.

## Scope delivered

- Keeps the v1 claim prefix and stable capability tokens unchanged.
- Rejects length prefixes with leading zeroes or any spelling that differs from the invariant decimal representation.
- Caps the entire grant claim value at 320 characters before parsing.
- Adds negative tests for noncanonical length prefixes and oversized values.

## Boundaries

This hardens claim parsing only. It does not issue grants, authenticate identities, resolve resources, configure an identity provider, or wire panel routes. Real authorization remains unavailable until trusted server handlers and resolvers are configured.

Tests use synthetic claims and references; they are not formal QA or production authorization.
