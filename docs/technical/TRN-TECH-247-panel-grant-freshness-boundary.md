# TRN-TECH-247 — Panel grant freshness boundary

## Evaluation order

Before checking a grant, the policy requires a non-null `PanelAccessFreshness` snapshot with every status set to `Confirmed`:

1. `SessionCurrent`
2. `SubjectMappingCurrent`
3. `PermissionVersionCurrent`
4. `GrantNotRevoked`
5. `AuditReady`

Each absent or rejected value fails closed with a distinct internal reason. Undefined enum values also fail because only `Confirmed` passes.

## Provenance

This type only carries evidence; it cannot authenticate or attest to its origin. A future integration must populate it from trusted server-side session, subject mapping, current grant/revocation, permission-version, and audit sources. The current repository does not configure those authorities, so this remains an internal contract and no panel data path is enabled.
