# Patient support requests

Figma frame 117:2 (/patient-app/support) now submits and reads requests through
GET/POST /api/v1/me/support-requests. The existing patient-web help link opens
this same connected route instead of a placeholder email address.

The authenticated OIDC issuer and subject own each request. Neither body owner
selectors nor another account's request IDs expose its requests. Request bodies
reject unmapped fields. Topics are account, group, appointments and privacy; a
trimmed description must contain 10–4000 characters. New requests have only the
persisted Registered state. No operator reply, resolution or notification is
fabricated, and no live message is sent to an external support service.

The PostgreSQL row stores a server-generated tracking UUID, caller request UUID,
normalized command fingerprint, topic/message and timestamp. A unique constraint
on issuer/subject/request ID serializes concurrent identical creates. Exact
retries return the original receipt; changed parameters conflict. The row is
both the persisted request and its creation receipt, committed before success.

GET returns 20 own requests at a time, using a server-issued nextBefore cursor;
optional requestId lookup reconciles an uncertain create without resending it.
The browser validates receipts against the submitted ID/topic/description before
claiming success. Lost or malformed responses lock new submission until the
current command is reconciled; definite 4xx failures preserve the editable form.
History refreshes and authorization failures clear previously displayed rows.
Dates use Persian formatting and Tehran time. Tokens/descriptions are not stored
in browser localStorage/sessionStorage. Emergency and unnecessary-medical-data
notes follow the Figma source, and the existing account-section assets are reused.

## Verification

Six new browser cases cover create/read/reload, lost-response reconciliation
without repeated POST, malformed receipts, missing authentication, history
pagination/denied refresh, and a 390px screenshot with local assets. The existing
profile/appointment suite runs with these checks. Four new PostgreSQL HTTP tests
cover own-account persistence, anonymous/cross-account denial, concurrent exact
retry/conflicting reuse, invalid content/owner selectors and nonoverlapping
history pages with exact receipt lookup. They are included in the feature CI.

This is patient request intake and tracking. Staff authorization, triage, replies,
notifications and closure workflows remain separate work. There is no deployment,
payment integration or main-branch merge in this increment.
