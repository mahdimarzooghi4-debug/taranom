# TRN-SP-58 — External identity TLS transport preflight, disconnected by default

**Date:** 2026-10-11. **Parent:** Sprint57 PR #65 Draft/Open. **Source:** TRN-BIZ-018/019/020/021/022/023/024; TRN-TECH-012 and TRN-TECH-002 T-01/T-03/T-08; national DENIED blocker inventory.

**Why not another synthetic medical projection:** The approved product needs genuine interactive member authentication and separately licensed specialist identity proofing. No actual vendor, contract, selected host, trust root, live user, entitlement or real identity integration currently exists. Independent provider selection cannot be synthesized from CI or by LLM. This Sprint introduces an *actually runnable, transport-only* operator preflight for a later explicitly selected hostname, making the next external-infrastructure verification step testable while refusing vendor invention.

**Bounded deliverable:** read-only SSL/TLS handshake utility for operator-selected external public hostname, no HTTP/credentials, fail-closed DNS/SSRF and CA validation, no host in reports, separate AUTHENTICATION_IDP vs IDENTITY_PROOFING labels, deny-only operational status and no implied proofing or auth authority; offline mock-only tests and CI no-target report. TRN-TECH-067, backlog and inherited 12-job workflow.

**Out of scope:** provider contract, user sign-up/authentication, KYC payload, OIDC, session/tenant mapping, membership activation, medical/billing/AI, real secret/environment integration, actual endpoint selection, Stage or Production. Even genuine successful TLS handshake is not approval. Sprint passes only with exact-head CI green + COMMENT technical Code Review, PR Draft/Open/unmerged, national first official launch DENIED_BY_DEFAULT.
