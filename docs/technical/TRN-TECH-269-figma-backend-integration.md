# Figma backend integration

Source: https://www.figma.com/design/78mOnfH4h9AX5Jk2ufRn0U/Tarannom?node-id=16-2

This branch combines the existing Figma frontend branch with the appointment API
branch. It does not merge either branch into main or deploy any environment.

## Integration inventory

| Figma area | Frontend | Backend status |
| --- | --- | --- |
| Patient app: appointment list (31:86) | /patient-app/appointments | Authenticated self-only list; future/past/cancelled filters |
| Patient app: appointment detail (91:2) | /patient-app/appointment-detail?booking=…&center=… | Lookup exclusively within authenticated own appointment list; cancel with confirmation |
| Patient app: requests (88:2) | /patient-app/request-tracking | Explicitly unavailable until a real request-state API exists; bookings are not pending requests |
| Center: schedule and bookings | /center-portal | Existing scope discovery, schedule read, booking read and slot definition connected to the merged backend foundation |
| Patient booking/search | /patient-app/booking and subsequent screens | Provider directory, qualification and city/specialty search are not implemented by current appointment API |
| Patient record, referrals, consent, support | Existing Figma routes | Remain previews; production APIs still required |
| Membership, group and finance | Existing Figma routes | Remain previews; identity onboarding, pricing and financial APIs still required |
| Admin and support institution | Existing Figma portals | Frontend present; production operations are not wired |
| Landing website | Excluded | No work requested in the prior scope |

The supplied patient canvas contains 43 frames, including two home variants.
Only the appointment list/detail are visually implemented in this increment.
Other existing preview screens have known visual differences and are not claimed
as Figma parity.

## Auth and hosting

The existing host integration is `window.taranomSession.getAccessToken()`. The
host must supply its real OIDC session. No production login, Keycloak client,
reverse proxy, grant bootstrap or server credentials are fabricated. API requests
are same-origin, no-store, credentials omitted and redirects rejected. Missing
session and denied access show errors, never sample patient appointments.
The standalone static preview cannot serve a .NET API: a deployment needs the
same-origin /api route mapped to the appointment API.

## Data and interactions

The list uses the server's scheduleRevision for cancellation, clears stale data
while refreshing and after failed reads, and aborts unmounted reads. Details only
select a booking from the authenticated account's returned list. No patient or
center selector is accepted by the list endpoint. Unknown details show not found.

Cancellation requires an in-page confirmation. While a command is running further
commands are disabled. Request IDs are retained per booking/revision in memory.
A failed write reloads server state and advises refresh before another attempt;
no optimistic cancellation is presented as confirmed. Filters use the current
load time and dates are rendered in Persian with Asia/Tehran timezone.

The API supplies opaque references, not provider names, specialty or addresses.
Those slots explicitly use references rather than inventing verified doctors or
copying sample identities from Figma. Booked does not claim a separate center
approval event. There is no unread notification dot without notification data.

## Verification

- Member, center and operations TypeScript/build checks.
- Five Playwright checks for list/filter/detail, unknown booking, denial and
  recovery, confirmation dismissal, successful cancellation and local asset geometry.
- Screenshot reviewed at 390×844. Figma logo, bell and all four navigation assets
  are local files. Font is Vazirmatn with its bundled OFL license.
- Browser tests use intercepted synthetic responses. They do not prove a live
  OIDC login or a deployed PostgreSQL/API/browser integration.
