# TRN-TECH-006 — Sprint 01 Reproducible Stack Candidate (C#/.NET)

- **Status: TECHNICAL CANDIDATE / PROPOSED FOR REVIEW — exact versions verified from official release/package sources; NOT yet installed, restored, built, or approved as Code stack**
- Date: 2026-10-09; architecture parent [TRN-TECH-005](04-hybrid-dotnet-python-architecture-decision.md).
- Purpose: replace the expired Python/FastAPI whole-backend candidate with a concrete **synthetic-only .NET Sprint 01** toolchain that reviewers can reproduce.
- Scope only: Identity/Access, independent test consent, append-only Audit, deny-by-default, PostgreSQL test database and **no external provider/real account/financial/clinical/AI operation**.

## 1. Candidate version matrix and verification

| Technology | Candidate exact version | Scope / source | Confidence |
|---|---|---|---|
| C# | 14, language version associated with .NET 10 | Operational modules only | framework baseline; compile not tested |
| .NET SDK | **10.0.401** | official https://dotnet.microsoft.com/en-us/download/dotnet/10.0 | release verified; availability on selected CI image untested |
| .NET runtime/ASP.NET Core | **10.0.12** | https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core | LTS supported to 2028-11-14; image tag/digest not selected |
| Target Framework | **net10.0** | Microsoft .NET / EF Core 10 | proposal; no project file yet |
| EF Core | **10.0.12** | https://www.nuget.org/packages/Microsoft.EntityFrameworkCore/10.0.12 | release verified; package restore untested |
| Npgsql EF Core provider | **10.0.3** | https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3 | targets net10.0; depends on EF Core >=10.0.4 and <11.0.0; end-to-end untested |
| PostgreSQL (test DB) | **18.6** | https://www.postgresql.org/support/versioning/ | supported release; container digest and managed provider not selected |
| xUnit test framework | **xunit.v3 4.0.1** | https://www.nuget.org/packages/xunit.v3/4.0.1 | package release verified; test runner invocation untested |
| HTTP API docs | OpenAPI versioning via ASP.NET Core tooling | transport for synthetic tests only if review approves | exact generator package/version OPEN |
| Integration tests | Testcontainers for .NET + PostgreSQL module | https://dotnet.testcontainers.org/modules/postgres/ | package version, runtime setup, digest OPEN |
| CI | GitHub Actions + pinned actions/container images | no workflow yet | OPEN |
| Python AI | **Not included in Sprint 01** | authorized direction from TRN-TECH-005 | all Python/model/library/version decisions OPEN |
| Web frontend | **Not included in Sprint 01** | React/TypeScript candidate from earlier proposal | final version/approval OPEN |

**Release evidence != executable compatibility.** Even a compatible NuGet dependency graph must pass clean package restore, build, migrations, integration tests and security tests before it is declared ready. Never label this candidate CI green.

## 2. Reproducibility rules for future Code Authorization

- **SDK lock candidate**: a future `global.json` should request `10.0.401` with explicit roll-forward policy (prefer exact for CI), after verifying the SDK is available. .NET target `net10.0` is distinct from the SDK version.
- **NuGet**: future `Directory.Packages.props` must pin exact vetted package versions; restore to a lock file and enforce locked restore in CI; review NuGet advisories, transitive constraints and SBOM. No floating minor/major wildcards for release builds.
- **Database**: PostgreSQL **18.6** is the candidate testing baseline; select a verified image by immutable digest before CI. Use role-separated credentials, schemas owned by modules and real PostgreSQL for constraint/transaction tests; EF in-memory provider cannot substitute for concurrent/DB-protection tests.
- **Migration safety**: schema changes require versioned EF migrations reviewed for up/down effect, data minimization, rollback/recovery and preservation of append-only audit history. No TestHarness principal or seed data may be provisioned in Production.
- **Toolchain**: exact Linux runner/container, compiler configuration (nullable, warnings-as-errors, analyzers), formatting, test commands, dependency scans and artifact attestation must be recorded in a subsequent Code PR and proven by CI. Do not invent image SHA, secret, vendor or host.
- **Scope isolation**: any synthetic mode is compile/deployment gated, disabled and unlinked in production. A runtime environment variable alone must not make test identity authoritative.
- **Version maintenance**: future patches require dependency-review, CI, migration smoke and explicit approval. LTS/version labels do not replace verification.

## 3. Planned solution layout (design only; no files of Code authorized)

```text
src/
  Taranom.Api/               # internal/non-public test entrypoint when accepted
  Taranom.Identity/          # identity and access policy owner
  Taranom.Consent/           # test consent evidence owner
  Taranom.Audit/             # append-only audit and enforcement owner
  Taranom.Infrastructure/    # PostgreSQL persistence, DI, migration boundary
tests/
  Taranom.UnitTests/
  Taranom.ContractTests/
  Taranom.PostgresTests/
  Taranom.SecurityTests/
```

The proposed layout is not a microservice deployment and does not make one module's DbContext free to modify another module's tables. Separate schemas or ownership mappings are subject to review. No Python or React project is created in the first Sprint.

## 4. Acceptance gate for the candidate

1. Technical reviewer validates versions/supply-chain, confirms `dotnet --info`, restore, build and independent test-runner operation **with actual logs**, not guesswork.
2. PostgreSQL migrations and all audit/concurrency constraints are proven in real PostgreSQL tests.
3. `TRN-TECH-003` 20 acceptance cases map to independently executed unit/contract/database/security evidence.
4. Code Review examines failure paths, masking of secrets/PII, tenant/subject isolation and no write access from Python/AI.
5. Explicit Product Owner **Sprint 01 Code Authorization** after contracts and CI plan pass the Technical gate; without it **remain in Technical**.

## Not decided

No actual package installation, image digest, database credentials, test runner wiring, UI framework approval, external IdP, identity proofing, financial contract, clinical approval, hosting/GPU, Python model, service-to-service transport or Stage/Production. Business PR #1 and Technical PR #2 remain Draft/Open.
