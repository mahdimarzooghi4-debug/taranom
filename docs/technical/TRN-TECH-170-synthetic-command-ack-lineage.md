# TRN-TECH-170 — DRAFT / exact-command acknowledgement integrity across three synthetic member journeys
Date: 2026-10-18. This is strictly a CI-only, local engineering boundary and NOT any Business approval, identity attestation, clinical consent, financial right or Production API.

## Failure mode
Existing ASP.NET/PostgreSQL synthetic member flows correctly preserve event/audit history, unique request UUID and idempotency, but React previously accepted a 200 response based mainly on revision and denial flags. A forged or mismatched ACK could clear the UI's uncertain-command latch even though the returned event was not the exact command just sent.

## Implemented bounded contract
- Care coordination (TRN-TECH-115) and late evidence (TRN-TECH-114): bind successful `RecordedUnverified` or `Replayed` to one and only one event with same UUID, expected event revision, action, original submission lineage and every original test-only command scope field. A fresh accepted command must finish on exactly expectedRevision+1; a legitimate replay may include later events.
- Proofing (TRN-TECH-113): project only the previously persisted, random, nonpersonal event request UUID (no new table or source). Browser validates the exact request UUID, case event revision, action, expected identity-version effect and resulting review; no false proofing authority.
- A mismatched successful ACK is treated as **uncertain**, not recorded. Component keeps the identical JSON+UUID in volatile memory, blocks new commands and allows only explicit manual same-command replay. No automatic retries, localStorage or external credentials.

## Acceptance
Real Chromium intercepts successful PostgreSQL-backed responses, deliberately corrupts a provenance field/UUID, verifies UI refuses to display success, then manually replays the same UUID to obtain the truthful journal result. Reuse all 14 mandatory full regression jobs. This is not real identity, treatment, evidence verification, funding, verified provider or production activation. National release always `DENIED_BY_DEFAULT`.
