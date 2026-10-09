# TRN-TECH-010 — Sprint 01 technical review and executable evidence admission

- Date: 2026-10-09
- **Review status: DESIGN REVIEWED WITH OPEN GATES / NOT CODE-READY**
- Exact document lineage: `TRN-TECH-003/005/006/007/008/009`; PR #1 and PR #2 remain Draft/Open.
- No implementation, CI workflow, real PostgreSQL test, signed code review or deployment exists in the repo as of this check.

## 1. External and local verification performed

| Evidence | Finding | What it proves / does not |
|---|---|---|
| Microsoft .NET 10 download | SDK `10.0.401` and runtime `10.0.12` listed | Official release exists; installed SDK and build result **not** proved |
| NuGet Microsoft.EntityFrameworkCore | `10.0.12` targets `net10.0` | package exists; restore, migration behavior **not** proved |
| NuGet Npgsql.EntityFrameworkCore.PostgreSQL | `10.0.3` targets `net10.0`, requires EF Core `>=10.0.4,<11.0.0` | direct dependency range compatible with `10.0.12`; resolution tests not run |
| PostgreSQL | `18.6` released 2026-08-13 | candidate server exists; no DB provisioned or integrity tests |
| NuGet xunit.v3 | `4.0.1` released/compatible with `net10.0` | test framework exists; test runner behavior not proven |
| Working execution environment | `dotnet`, `docker`, `psql` not found on inspected PATH | **Local runtime execution not available**; cannot truthfully claim compilation/migrations/tests passed |
| GitHub exact Technical HEAD before this change | No workflow runs or status checks | **No CI evidence**, not CI green |

Official references: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 ; https://www.nuget.org/packages/Microsoft.EntityFrameworkCore ; https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL ; https://www.postgresql.org/docs/release/18.6/ ; https://www.nuget.org/packages/xunit.v3/4.0.1 .

## 2. Risk review and mitigations

| Finding | Severity for actual Code | Required gate / test |
|---|---|---|
| Client could assert synthetic role/test-origin | BLOCKER | test-only trusted context isolated from runtime; ID-04/08 X-06 |
| Sponsor accessing member's PHI or wallet | BLOCKER | deny-by-default cross-subject/tenant; ID-02/05/06 X-07 |
| Research consent implicitly inferred from clinical consent | BLOCKER | separate purpose with no cross-purpose promotion; C-01/02/06 |
| Interrupted audit write leaves consent mutation | BLOCKER | PostgreSQL transaction proof; A-03 X-03 |
| Concurrent grants/withdrawals lose version | BLOCKER | DB optimistic concurrency/uniqueness; C-04 X-01 |
| Replay changes payload or duplicates audit/state | BLOCKER | durable, scoped idempotency; A-05 X-02 |
| Audit modification or sensitive data leakage | BLOCKER | DB permissions + direct SQL and allowlist; A-02/04 X-04/08 |
| Prototype identity accepted in real operation | BLOCKER | assembly/package exclusion and no real adapters; B-01 X-06 |

Review above is **document-based engineering risk review**, not a security penetration test or independent code review.

## 3. Exact engineering gates remaining

1. **Toolchain gate**: select/approve .NET 10 candidate and exact package locks, verify installation in appropriate CI and successfully restore/build with recorded outputs.
2. **Contract gate**: review `TRN-TECH-009` internal V0 into V1: define test-only trusted context, DTO, owner permissions, result/error compatibility, DB versioning/replay and security tests. No invented public endpoints.
3. **Persistence gate**: exact migration/schema and Postgres transactional concurrency semantics, audit append-only protection and isolation. Test `A/X` cases with real PostgreSQL; no SQLite/in-memory substitute.
4. **Authorization gate**: **explicit owner instruction to start Sprint 01 Code**, confined to synthetic .NET identity/consent/audit. No implicit permission from approved business strategy.
5. **Evidence gate for implementation**: Code branch Draft PR, CI exact SHA, secure tests, independent review. Stage/QA/Release/Production remain blocked pending respective authorization and legal/technical readiness.

## 4. Product handoff

- `TRN-SP-01` planning is sufficiently concrete to identify contract and security tests; **no additional Business micro-question is needed**.
- For immediate progress, the next substantial work is **contract V1 approval + Code Authorization**, followed by real .NET code/test/CI. Do not generate more Business choices or pretend doc review equals green CI.
- The accepted hybrid architecture (C#/.NET core and Python internal AI) is preserved; **Python, clinical functionality, insurance, real finance and real identities are excluded from the first Sprint**.

**Admission verdict: NOT CODE-READY.** The explicit Code start decision and actual executable evidence remain the hard next gates. No merge.
