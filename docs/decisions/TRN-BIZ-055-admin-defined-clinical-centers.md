# TRN-BIZ-055 — Admin-defined clinical centers

- **Status:** ACCEPTED — explicit product-owner clarification on 2026-11-06.
- **Related decisions:** TRN-BIZ-043, TRN-BIZ-044, TRN-BIZ-053, TRN-BIZ-054.
- **Scope:** ownership of the canonical clinical-center record in Taranom. This decision does not establish clinical qualification or legal authority.

## Business rule

1. **Taranom Admin creates and defines each canonical clinical-center record.** A center cannot self-register or create its own canonical center identity.
2. A center-scoped manager account and permissions must be associated with an already defined center. A center's own draft submission cannot create a canonical center or grant itself access.
3. After the center exists, the center manages its own departments and its own unverified physician affiliations under TRN-BIZ-053/054. One center may have multiple departments; no independent chain/network parent or cross-center management role is introduced.
4. The existing center profile draft is a center-submitted, unverified claim only. It does not directly change the canonical Admin-defined center record and is not eligible for public/member discovery. Any workflow for Admin review and acceptance of profile changes remains to be specified; no automatic acceptance is implied.
5. Admin creation establishes that a center record exists in Taranom. It does **not** establish the center's legal identity, operating licence, service permissions, professional quality, contract with Taranom, national coverage, or eligibility to receive treatment-credit payments. Those remain separate evidence and accountable-human decision gates.
6. Member-facing discovery/publication is not automatic from Admin creation. It requires the relevant approved publication rules and, where availability is shown, authoritative live capacity. A draft profile, a doctor claim, or the mere existence of a center record is not proof of available clinical service.

## Boundaries and unresolved contracts

The following must be refined before the corresponding implementation is treated as production-ready: canonical center fields and uniqueness rules; Admin authorization and audit requirements; stable center-reference issuance; center-manager association and grant provisioning; center-record correction/suspension/closure lifecycle; the Admin review process for center-submitted profile changes; legal identity/licence evidence; and the separate gate for public/member publication.

Do not invent field schemas, approval states, legal verification sources, automatic publication, or service/coverage claims. The existing synthetic center and center-directory draft implementations do not implement this Admin-owned canonical registry.

## Internal implementation slice (not external operating authorization)

The center creation path is now Admin-only in the authenticated Appointments
API and uses an explicit `AdminCenterRegistryManage` server-side grant on
the fixed `taranom-center-registry` scope. It is **not** granted by role
claims, requests, self-registration or default configuration. A separately
authorized provisioning process for the first real Admin remains an external
governance prerequisite.

Admin may enter an organizational display name and city (minimal Figma
directory metadata, not a legal entity registry) and submit one UUID request.
The backend issues an opaque immutable center reference, an append-only
PostgreSQL creation receipt and a private read model. The same request/payload
replays to the same center; conflicts, revocations and unknown receipts fail
closed. No managerial account/grant is created automatically. A real manager
association requires a verified subject mapping and an explicit later
authorization contract. The old center-controlled draft source is separate
and cannot update canonical Admin-entered fields.

Neither Admin creation nor the registry record supplies licensure, legal
identity, quality approval, clinical access, national availability, member
publication, contracts or treatment-credit payment permission.
