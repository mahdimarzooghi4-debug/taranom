# Sprint 101 — Isolate support-case PostgreSQL runtime rights

**Date:** 2026-10-13 | **Technical:** [TRN-TECH-110](../technical/TRN-TECH-110-support-least-privilege-runtime.md) | **Business:** TRN-BIZ-045/048/050 | **Epics:** TRN-EP-01/10/12/13.

**Parent:** Draft/Open Sprint100 PR #111 HEAD `4a969d4f2acc50907aeaf7846f2a767baf747a4c`, Push #38387221139 and Mainline PR #38387270470 both 14/14 SUCCESS; preliminary COMMENTED review only, no independent human approval.

**Observed security gap:** support GET/POST received the same privileged datasource that performed schema creation and trigger DDL. Introduce a dedicated synthetic-only PostgreSQL login, owner-only schema bootstrap, exact limited grants for header CAS and append-only event/audit; route *all* existing support HTTP endpoints through the restricted datasource. Startup fails if runtime user identity is privileged or wrong. All existing endpoint schemas and business outcomes remain unchanged.

**Real acceptance:** extend existing direct PG + Kestrel MVCC probe: authenticated restricted runtime checks, 10 expected PostgreSQL 42501 denials for history erasure, trigger tamper, truncation, case deletion, cross-context SELECT and schema CREATE, permitted support history SELECT, followed by genuine mixed support reads/writes with 11 accepted paired audits. Existing full 14-job Push/Mainline CI, Chromium/PG/recovery, and launch-denial controls unchanged. Initial COMMENT code review after both exact-head CI green; this is not a complete compromise-resistant model or Production entitlement.

**No merge/deploy:** Keep PR and stack Draft/Open/Unmerged, 17 national blockers BLOCKED_UNVERIFIED, release DENIED_BY_DEFAULT, completion UNBASELINED. No real member, medical, legal, insurance or financial integrations; TRN-TECH-012 remains NOT CODE-AUTHORIZED.

**CI follow-up:** a static secret-history check rejected a checked-in password assignment in the new probe; fixed only this security issue by adding a per-job random ephemeral role password and requiring it for host/bootstrap/probe without bypassing the scanner. Full CI must rerun on the exact corrected SHA.
