# TRN-TECH-125 — Sprint 116 support history Web → .NET → PostgreSQL

Status: isolated demo/CI only. Reuse the existing TRN-TECH-084 paginated read-only support history API and the same persisted TRN-TECH-075 support journal. No third-party APIs, new financial/clinical/business decisions, new identity authorities, or fake approvals.

## Acceptance
- Only after verified support snapshot, the existing center-demo React app may view the matching case's events from its existing .NET route `/internal/demo/v1/support/{caseRef}/history?afterRevision=N&limit=3`.
- Verify the immutable source flags, exact case identity, pinned revision, monotonic contiguous page versions, schema and cursor before rendering. Source outage or mismatch clears prior events and blocks further navigation.
- No claims about member authentication, official complaint registration, insurer interactions, clinical review or release. No new HTTP route is created.
- Three Chromium regressions: real localhost .NET/PostgreSQL journey, forged authority denial, and outage clearing stale events. Full 14-job shared CI; independent human Code Review still needed.
- The demo is restricted to `CI_EPHEMERAL_ONLY`, test references and isolated localhost PostgreSQL. This is not member-facing production authorization. Stage/QA/release/Production not authorized.
