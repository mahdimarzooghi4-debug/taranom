# Patient web appointment integration

The existing Figma patient-web appointment route (/patient-portal/appointments,
frame 261:2) now uses the same authenticated appointment API as the mobile app.
The old appointment sample cards and hard-coded summary counts are removed.

- GET /api/v1/me/appointments derives identity at the server. The browser does
  not send a member selector. Errors and refreshes clear previously shown data.
- Upcoming and history groups, actual summary counts and inline details use the
  returned bookings. Elapsed dates are not presented as completed clinical visits.
- Cancellation requires a separate confirmation, uses the returned schedule
  revision and a retained in-memory request ID, and re-reads server history.
- The common cancellation client now validates Applied/Replayed, revision,
  appointment ownership references and cancelled state (numeric 2 or string)
  before showing confirmed success. HTTP 200 alone is not treated as success.
- The repeat-booking link carries only the returned center reference into the
  connected mobile slot selection flow. City/specialty search remains disabled
  until directory APIs exist. Pending request counts remain unknown, not zero.
- Fictional account names are not shown beside authenticated appointments on
  this route. Other existing preview routes and layout differences remain out
  of scope (notably existing sidebar placement and active-tab color). A broken shared logo file was replaced with the already downloaded
  valid Figma logo, not a new illustration or synthetic substitute.

## Verification

Member TypeScript/build checks and 18 browser checks cover mobile history,
booking and the five new web cases (read/count/detail/cancel, denied refresh,
empty history, malformed cancellation receipt, 1440px assets/overflow screenshot).
Browser tests use intercepted synthetic responses and are not live database
proof. The 1440px screenshot is inspected for this route.

The .NET API and contract-test project compile locally. Eighteen local .NET tests
cover appointment domain and OIDC authentication middleware/configuration and
pass. Local PostgreSQL is unavailable in this container; CI now runs existing
HTTP and PostgreSQL-store tests against an isolated PostgreSQL 18.6 service on
this feature PR. This makes actual database verification independent of a
user-owned server, payment gateway or deployed environment. It does not imply
production login or operational readiness.
