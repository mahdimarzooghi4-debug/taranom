# TRN-TECH-267 — Authenticated member appointment list

GET /api/v1/me/appointments derives ownership only from the authenticated OIDC issuer and subject plus active MemberAppointmentsManage grants whose subject mapping, permission version, revocation, and audit evidence remain current. The request has no memberRef or centerRef selector. PostgreSQL returns only booking objects whose memberRef matches one of those current grants across center schedules.

The response contains opaque booking, member, and center references, appointment start time, state, and the center schedule revision. It sets Cache-Control: no-store. The endpoint returns no patient profile, provider directory, or clinical information. A cancellation remains visible as CancelledByMember in the patient's own appointment history.

The endpoint is an appointment-history API only. It does not find centers or doctors by city/specialty, and it does not establish a browser login session. Frontend callers still need a valid OIDC bearer-token source; no token is stored by this endpoint or fabricated for the UI.
