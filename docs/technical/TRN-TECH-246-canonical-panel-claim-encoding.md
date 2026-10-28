# TRN-TECH-246 — Canonical panel claim encoding

The v1 grammar remains `v1|<scope-length>|<scope>|<capability-token>`. Sprint 246 preserves all existing stable capability tokens and requires the decimal scope length to round-trip exactly through invariant formatting. Encodings such as a leading-zero length are rejected.

The complete claim value is limited to 320 characters before parsing. This exceeds the maximum valid payload implied by the 256-character scope bound and the current capability tokens, while bounding malformed input.

This remains an input-format check, not cryptographic validation. Trust still depends on a future authenticated principal, trusted issuer configuration, and server-side resource resolution. No IdP, issuer, resolver, route, role assignment, or production gate is added.
