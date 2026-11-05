# Authenticated member appointment history client

`MemberApp` accepts an optional `appointmentAccess` with an asynchronous
`getAccessToken()` supplied by the authenticated hosting application. On the
appointments route this enables `MemberAppointments`, which calls the existing
same-origin `GET /api/v1/me/appointments` endpoint. It never sends member or center
selectors, persists tokens, or follows redirects with the bearer credential.

The component renders loading, empty, booked, cancelled, signed-out, forbidden,
and retryable failure states. Dates use Persian formatting and Asia/Tehran time.
Requests are aborted when leaving the route or replacing access; data from an old
access object is hidden immediately. The host must replace the access object when
its authenticated identity changes or sign-out occurs. Do not reuse the same
access object across accounts.

The standalone preview still supplies no access provider and retains its
existing disconnected CSP. This change does not establish a browser session,
configure a reverse proxy, or claim a live OIDC connection. A connected host must
supply its real token provider and serve the API on the same origin under a
separately configured CSP. Browser login/session integration remains outstanding.
No doctor directory, booking, cancellation command, or clinical data is added.

Validation: `npm run test:appointments` covers missing session, self-only endpoint,
non-caching and redirect behavior, successful/empty history, HTTP 401/403/500,
invalid response structures and abort during token acquisition. `npm run check`
checks TypeScript and builds the member application.
