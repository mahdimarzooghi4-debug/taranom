# TRN-TECH-265 — Appointment change audit

The appointment PostgreSQL store appends a success audit row for every applied command: slot definition, booking, and member cancellation. It obtains the command receipt from the resulting immutable schedule snapshot and writes the audit event before transaction commit. The schedule snapshot and event commit together; a failed audit insert rolls back the schedule mutation.

Each event includes an event UUID, center-scoped request ID, authenticated OIDC issuer and subject, action, opaque target reference, optional appointment reference, SHA-256 command fingerprint, resulting schedule revision, and database timestamp. A unique key on `(center_ref, request_id)` prevents duplicate success events for one command within a center. Exact retries return the prior result and do not insert another audit row; conflicting or denied commands do not create success events.

The application exposes no update or delete route for these rows. This is not a claim of database-level append-only enforcement or independent evidence custody; production database roles, backups, retention, access monitoring, and legal retention policy remain operational decisions. Reads are not logged by this slice. No clinical note, diagnosis, contact detail, or physician profile is stored in the audit event.
