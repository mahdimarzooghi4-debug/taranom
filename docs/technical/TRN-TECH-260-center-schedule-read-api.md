# TRN-TECH-260 — Center schedule read API

`GET /api/v1/centers/{centerRef}/appointment-slots` returns the stored slot collection and schedule revision for one center. It uses the same `CenterAppointmentsManage` policy and server-side grant lookup as slot creation and center appointment reads. The path scope must match the active central grant; caller-provided role or scope data is not trusted.

Slots are ordered by their start instant. The result includes open and booked slots so the center can reconcile its schedule with bookings; it contains opaque slot, doctor and member references only. An uninitialized schedule yields revision `0` and an empty array.

The HTTP regression creates a slot and reads it back through the center route. No directory search, member-visible center profile, real names, provider enrollment, insurer integration or production access is introduced.
