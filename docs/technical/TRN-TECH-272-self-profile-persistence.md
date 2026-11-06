# Self-owned patient profile persistence

Figma frame 99:2 now uses GET/PUT /api/v1/me/profile. The personal-info read page
and existing web account route use the same self-profile data. Existing account
sample membership status is removed from the connected web profile route.

The owner is exclusively the authenticated OIDC issuer and subject. Neither a
URL member selector nor a body owner identifier is accepted. The request model
rejects unmapped fields, including nationalId and phone. The API accepts full
name, optional ISO birth date and city as self-declared information. This table
is not official identity proof, age eligibility, membership activation, treatment
consent, a patient directory or financial authority. Nothing grants appointment
capabilities or activates membership when profile data is written.

PostgreSQL stores one profile per issuer/subject. An identity-scoped transaction
lock serializes updates, and ExpectedRevision prevents lost updates. A UUID
request ID plus normalized command fingerprint distinguishes exact retries from
changed requests. The profile update and audit receipt commit atomically. Audit
receipts store fingerprints and revisions rather than copies of profile values.
Reads and successful writes send Cache-Control: no-store.

No profile returns revision 0 and blank fields. Names are required and limited
to 140 characters, city to 100; control characters and impossible/future birth
dates are rejected. Date input uses a native Gregorian date control with an
explicit label; the read page renders Persian dates. Mobile form layout reuses
the downloaded Figma logo/header and adds the exact active-account and inactive
calendar SVG variants. National ID/phone remain locked and show unavailable
identity-source status rather than fabricated identifiers.

The UI reads back after saving. Definite conflicts require a fresh profile before
saving again. Lost write responses or failed read-back lock additional saves
while the user reconciles the current profile. Tokens, commands and personal
values are not stored in localStorage or sessionStorage.

## Verification

- API and contract-test project compile locally with no warnings/errors.
- Six new browser cases cover read/write/reload, locked fields, stale revision,
  uncertain response reconciliation without another write, shared web ownership,
  missing login and mobile screenshot/assets. The combined 24 browser tests pass.
- Four PostgreSQL/HTTP integration tests cover authentication and owner isolation,
  no activation/grants, replay and stale/conflicting requests, locked-field/date
  validation and concurrent compare-and-swap. CI runs these on PostgreSQL 18.6.
- Existing HTTP and new profile HTTP classes share a test collection so their
  API-host bootstrap operations do not race against each other in CI.

This is self-profile editing only. Live identity-provider login, verified phone
and national identity, family management and membership/payment flows remain
separate. No deployment or main-branch merge is performed by this increment.
