# TRN-TECH-067 — Operator-specified external identity HTTPS/TLS connectivity preflight

**Date:** 2026-10-11. **Baseline:** Sprint57 Draft PR #65, exact-HEAD full Push and PR CI 12/12 green and preliminary COMMENT-only Code Review. **Accepted source:** TRN-BIZ-018…024, TRN-BIZ-028/030/035/036/040/041; TRN-TECH-002 T-01/T-03/T-08; TRN-TECH-012 candidate member session boundary; TRN-TECH-045 national DENY; TRN-TECH-066 public workflow trust guard.

## Actual external blocker and separation of duties

TRN-BIZ-019 approves the *category* of a direct-contracted licensed specialist proofing operator, NOT any named provider, API, contract, authentication IdP, subject identifier or login flow. Identity **proofing** and interactive member **authentication** are independent purposes. Neither can be inferred from a green CI suite. Actual host/domain, DNS ownership, TLS trust root, legal arrangement, signed operator authority, supplier suitability, callback registrations, login policy, token validation, data rights and member/tenant mapping are all still OPEN. **Do not choose or infer them in Code.** No live identity API, source database, OAuth/OIDC flow, session cookie, bearer token, KYC payload or Proofing result is implemented.

## Implemented read-only operator diagnostic

A small stdlib utility \`tools/policy_checks/check_identity_tls_preflight.py\` provides a concrete **operator-invoked, outbound TLS-only** transport diagnostic. It requires explicit purpose (\`AUTHENTICATION_IDP\` or \`IDENTITY_PROOFING\`) and, for actual network contact, an **operator-selected public DNS hostname**. It never stores hostname in the report, never sends HTTP requests or credentials, never follows redirects, never reads actual people or provider responses, and cannot select a provider. The GitHub CI invocation omits host entirely and emits \`TARGET_NOT_CONFIGURED\` — **not a successful connectivity claim**.

An actual operator run strictly rejects URLs, path, port, userinfo, IP literals, control/unicode/invalid/reserved hostnames, private/reserved loopback or link-local DNS destinations and mixed public/private DNS answer sets. DNS is resolved once and the numeric public IP is used for the socket connection; the original hostname is checked through SNI/TLS hostname verification against system trust roots. Require TLS >=1.2, nonempty unexpired peer certificate, peer numeric address equal to the pinned resolution, and no credentials/HTTP payload. Failure reports fixed redacted reason labels; success is only \`TLS_TRANSPORT_REACHABLE_ONLY\`. This is **not** legal provider approval, authentication correctness, validated identity, TLS pinning, contract possession, national capacity, or security evaluation of the full service.

The report *always* includes \`identity_proofing_verified=false\`, \`member_authentication_verified=false\`, \`external_provider_contract_verified=false\`, \`member_activation_allowed=false\`, and \`national_release_allowed=false\`. It contains no provider host/IP, certificate content, personal data or secret. No Production, Stage, admin, network adapter or runtime integration is added.

## Usage and independent gates

Offline CI (no external target, no network): \`python3 tools/policy_checks/check_identity_tls_preflight.py --purpose AUTHENTICATION_IDP --report-json TestResults/sprint58-identity-preflight-unconfigured.json\`. It must yield \`TARGET_NOT_CONFIGURED\`, false connectivity and false release.

Only **after selecting and authorizing an actual service** by separate human process may a custodian on a trusted private operator machine run \`python3 tools/policy_checks/check_identity_tls_preflight.py --purpose AUTHENTICATION_IDP --host <operator-approved-public-host> --report-json <private-output>\` (replace placeholders privately; do not put real host in a public PR log). This issues a single transport-level TLS connection with no HTTP request; it **does not** validate OIDC discovery, JWT signature/issuer/audience, proofing licence, data-processing contract, recovery, MFA, tenant rights or member consent. A successful TLS probe cannot authorize any live login or actual KYC data exchange.

## Admission and tests

Only mock socket/DNS/TLS interactions in CI, positive numeric IP pin and valid SNI test, negative private/mixed answers, invalid host and URL/userinfo, unexpected DNS, timeout, stale certificate, untrusted peer, address substitution, weak TLS and report redaction. Preserve all 12 inherited .NET/real-test PostgreSQL/Chromium, public source history, workflow trust, external AI ban and national DENIED tests.

**Always deny official comprehensive nationwide launch across 3 markets and 17 dimensions until actual independently audited evidence and explicit human release approvals exist. PR Draft/Open/unmerged, no Ready/Merge/Stage/QA/Production.**
