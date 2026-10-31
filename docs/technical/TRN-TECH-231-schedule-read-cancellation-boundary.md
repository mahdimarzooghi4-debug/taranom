# TRN-TECH-231 — Schedule Read Cancellation Boundary

## Purpose

Make caller cancellation responsive across the provider-neutral schedule read boundary.

## Contract

- A canceled caller token is observed before query validation or source configuration checks.
- The source adapter receives the caller token.
- The read service stops awaiting the adapter when the caller cancels, even if an adapter fails to honor its token.
- Caller cancellation propagates as cancellation and is not rewritten to `SourceUnavailable`.
- The source port remains read-only; adapters are still required to observe cancellation to release their own resources.

## Boundaries

Cancellation does not cancel or mutate a booking, grant a member identity, authenticate a provider, or create availability evidence. The CI-only route remains unconfigured.

## Verification

MemberGovernance unit tests cover cancellation before validation and a delayed adapter that ignores the token. Existing tests preserve source failure classification and caller cancellation behavior.
