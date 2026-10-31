# TRN-TECH-214 — Member Appointment and Prescription Routes

**Status:** Draft implementation; disconnected UI only.

## Implemented routes

- `/member/appointments`: states that scheduling is a core Taranom service, but does not show availability, claim a booking, or submit a request without member identity and an authoritative center schedule.
- `/member/prescriptions`: states that a legally issued prescription should also be recorded in the member's Taranom record; no prescription or medication data is displayed here.
- `/center/appointments`: one center-level route for future schedule and appointment handling; it does not invent doctors, slots, or bookings.

## User authority and safety boundaries

The member chooses the doctor. The assistant may help with screening/referral but cannot choose or book on the member's behalf. A center's doctor affiliation is not proof of license, clinical privileges, contract, or schedule authority. Prescription issuance, modification, and renewal remain with an authorized prescriber.

## Integration prerequisites

No member identity provider, provider registry, clinical record store, e-prescription source, insurer API, or real appointment schedule is connected by this change. The routes are explicit unavailable states and send no API requests. Building the actual end-to-end appointment and prescription flows requires their owning systems and lawful data/authority contracts; no fake records or successful actions may stand in for those integrations.
