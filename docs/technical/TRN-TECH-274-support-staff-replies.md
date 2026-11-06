# Support staff reply integration

Adds `/admin/support-requests` to the existing operations portal layout. This is a functional extension using the existing admin visual patterns; there is no corresponding support-desk frame in the supplied Figma. Other administrative pages remain previews.

Authenticated members can read their own persisted replies in `/patient-app/support`. No subject/issuer selector is accepted for member ownership. State is Registered (revision 0, no answer) or Answered (revision 1, immutable answer and timestamp). Answered does not mean Closed; reopening, conversation threads and editing answers are deferred.

Operations GET `/api/v1/operations/support-requests?before=&requestRef=` reads a keyset page (20) or an exact ticket. POST `/api/v1/operations/support-requests/{requestRef}/reply` takes requestId, expectedRevision=0, reply (10–4000 characters). Both require a current central `SupportRequestsManage` grant with fixed scope `taranom-support`. Queue and writes are restricted to the staff identity's trusted issuer. No client roles, center-manager grants, or request parameters confer this capability. No automatic grant or runtime bootstrap is introduced.

Transactionally serializes each actor's command receipt, locks the ticket, checks revision, writes one answer and audit receipt. Exact retries replay after Answered; changed payload/id reuse and competing answers conflict. Read and write responses use no-store. On transport failure the UI checks the exact ticket and does not automatically repeat POST. A readback can establish the current persisted answer; it does not claim a particular actor authored it.

Existing databases are upgraded with additive reply/revision columns plus replacement of the Registered-only state constraint. Existing intake rows remain valid. Bootstrap follows the existing single-process startup schema pattern; a separate deployment migration mechanism is still pending.

Verification: focused browser coverage includes staff response/readback, missing session, forbidden access, stale write, lost-response reconciliation, and patient answer visibility. PostgreSQL HTTP tests include owner isolation, central scope/freshness, concurrent exact replay, changed command rejection, competing writes and missing ticket. Capability catalog tests include the ninth capability.

No payment, external notification, production identity activation, deployment or merge is performed by this increment.
