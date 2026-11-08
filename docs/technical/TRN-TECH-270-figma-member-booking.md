# Patient capacity selection and booking

This increment wires Figma frames 77:2 (capacity selection) and 80:2 (review) to
existing appointment API endpoints. The list/detail page links to capacity
selection using the center reference returned by the patient's own history.
A future provider directory can enter the same route with its selected center.
No center reference is fabricated, and missing center context sends no requests.

## Flow

1. Discover current MemberAppointmentsManage scopes from authenticated
   GET /api/v1/me/appointment-scopes. No member identity is taken from the URL.
2. Read open slots for the chosen authorized member scope and center using
   GET /api/v1/member/members/{memberRef}/centers/{centerRef}/appointment-slots.
   Accept numeric Open=1 and string Open responses. Reject malformed schedules.
3. Exclude elapsed slots; group days using Asia/Tehran and render Persian times.
4. Select a returned slot and review its actual center/doctor/member references.
   Provider names and patient profiles are unavailable and are never invented.
5. On explicit confirmation send one request ID, the schedule revision and slot
   reference to the member appointment POST endpoint. The existing backend
   transaction checks availability and revision; selection itself is no hold.
6. Validate Applied/Replayed and the returned appointment references before
   showing confirmed success. Open details using that returned booking ref.

If the server returns a definite 4xx refusal, clear the selection and reload
capacity. If the response is lost or invalid, lock further booking commands.
The patient can reconcile against authenticated own appointment history without
sending another POST. A matching current booked center/member/slot/time resolves
success; otherwise the result stays uncertain. Reads are aborted when unmounted,
and duplicate submit clicks are guarded synchronously as well as visually.
No command/token/clinical data is stored in localStorage or sessionStorage.

The mobile preview's old capacity/review routes now enter this connected flow.
Opening those routes without a center displays a real missing-context state
rather than demo time buttons. The existing city/specialty/provider directory
remains a preview and is not claimed as connected. Separate center approval or
pending-request state is not invented for a directly booked appointment.

## Validation

Eight additional browser checks cover selection/review/booking/details, stale
capacity conflicts, uncertain-write read reconciliation without repeated POST,
missing center, changing selection before submit, and screenshots/local assets
at 390px, denial when no active member grant exists, and an unresolved uncertain result. The existing five patient browser tests also pass. Tests intercept
synthetic API responses; live OIDC/API/PostgreSQL deployment remains outstanding.
Member TypeScript/build checks pass. Images were reviewed for both added screens.
