# TRN-TECH-258 — PostgreSQL tests for appointment persistence

The canonical full-regression workflow starts an ephemeral PostgreSQL 18.6 service for the internal contract job and provides its connection only as `TARANOM_TEST_PG`. `AppointmentPostgresStoreTests` uses unique opaque center and member references and creates no real identity, patient or provider data.

The tests cover persistence across separate `NpgsqlDataSource` instances, booking, owner-only cancellation, cancelled booking history, per-center isolation, stale revisions, and two concurrent writers racing from the same revision. The database is disposable CI state and does not constitute a production database, migration plan, operational backup, or formal QA environment.

HTTP endpoint behavior and OIDC/central grant integration still require separate host-level tests. No central grant is inserted by these tests, and no browser or production access is enabled.
