# Sprint 240 — Versioned prescription copy and panel capability boundary

## Scope

- Preserve a bounded opaque copy of a source-issued prescription as a synthetic versioned record. A content hash is only byte-integrity evidence.
- Make retries idempotent and reject conflicting request IDs, stale revisions, cross-member or cross-record appends, live references, oversized documents, and non-UTC issue times.
- Add exact-scope capability checks for the owner-confirmed member prescription, center roster/appointments/revenue, Taranom fund, and supporter case-finance surfaces.
- Keep role names, role hierarchy, user authentication, actual financial shares, insurer APIs, and payment operations outside this contract.

## Boundaries

The prescription document is not authored or interpreted by Taranom; issuer identity and authority are not verified. These pure contracts use only synthetic references and do not persist to an operational store. No clinical use, dispensing, real identity, medical record access, budget credit, settlement, or production permission is added. The panel policy is a capability contract, not real authentication or a production RBAC configuration.

## Validation

Adversarial xUnit coverage exercises immutable version history, idempotent retries, conflict and stale revisions, content bounds/hash, exact tenant/scope separation, and default denial.
