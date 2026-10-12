# TRN-TECH-045 — Comprehensive national release blocker inventory (DENY-ONLY)

**Date:** 2026-10-11. **Stacked parent:** Sprint35 Draft PR #42. **Business decisions:** TRN-BIZ-028 (three markets), TRN-BIZ-030 (all major capabilities at first official launch), TRN-BIZ-040 (national scope from first official launch), TRN-BIZ-041/042/043/048 (independent regulated operation and real integrations). This is a **technical admission denial proof**, not a real readiness certification.

## Problem

The repo has many green synthetic .NET, real-*test* PostgreSQL and disconnected browser CI runs. Those are **not** evidence that any nationwide clinical, insurance, financial, legal, identity or Production-service capacity exists. The inherited product backlog `TRN-PB-001` has **13 epics**; three accepted market segments and national scope also must NOT silently disappear from a so-called 'MVP' or auto-pass because a previous Sprint's unit tests succeeded.

## Implementation

The source-controlled `docs/governance/national-launch-blockers.json` lists exactly 17 distinct mandatory launch dimensions:

- **TRN-EP-01…13:** Identity, consent, membership, provider registry, care, records, insurance, finance, billing, quality/complaints, internal AI, operational readiness and Member Web;
- **MARKET-PUBLIC**, **MARKET-SUPPORT**, **MARKET-CORPORATE**: the three approved first-release markets;
- **NATIONAL-COVERAGE:** approved nationwide official-release goal, **not** established provider/insurer capacity.

Every item contains human-readable name, accepted **TRN-BIZ-###** document provenance and a finite list of **missing external prerequisites** rather than fake actors/insurance companies/hospitals, percentages, benchmark thresholds, service-level agreements or synthetic evidence representing genuine legal review. Every item's only currently permitted status is **`BLOCKED_UNVERIFIED`**. Overall release admission is **`DENIED_BY_DEFAULT`**, real release approval observed **false**, external partner evidence verified **false**. No credential, medical record, purported signed legal approval or unknown implementation is invented. A business policy accepted at *strategy level* is never treated as proof of actual service readiness.

`tools/policy_checks/check_national_launch_blockers.py` (Python 3, stdlib only) is a **fail-close schema/provenance invariant checker**:
- Require all 13 epic IDs, all 3 markets and national scope, exactly once, with fixed comprehensive-national policy refs. Reject missing/duplicate/unknown IDs or schema fields and changed status to `READY`.
- Require at least one **specific mandatory missing evidence category for EACH dimension**, permit only enumerated blocker categories, refuse removals of irreducible blocker, empty list, duplicates, or fake approval/production attributes.
- Verify all cited policy files resolve uniquely to actual local `docs/decisions/TRN-BIZ-###-*.md` sources; reject missing/invalid/ambiguous or symlinked decision refs; no network or content scraping.
- Parse JSON strictly (duplicate keys and nonfinite values are errors); deterministic sorted denial reasons. A CLI validation exit 0 means **the blockers are correctly documented**, NOT any approval, legal authorization or release. Attempted self-certification and malformed/unavailable registry exit nonzero.
- `--report-json TestResults/sprint36-national-blockers.json` emits a deterministic **release_allowed=false, evidence_verified=false, release_admission=DENIED** inventory of all 17 *unverified* dimensions with missing-evidence category labels, suitable for CI evidence attachment. It contains NO member/patient/provider PII, account IDs or credentials.
- No notion of approval or release promotion is executable in this tool. To support real evidence later, an **explicitly authorized, reviewed new business/technical contract and human approval workflow** must replace the DENY-ONLY gate; modifying JSON to say READY cannot activate anything.

## Verification and workflow

Add **28 standalone Python unit tests** for the full manifest and real checked-in provenance, every market and national scope, missing/duplicate items, omission of necessary per-domain evidence, attempted self-approval, invalid refs, symlinked/missing source, duplicate JSON, NaN, altered schema and CLI denied report. The tests create only fake policy documents in ephemeral temporary folders. The inherited **12-job Sprint36** workflow runs these tests, verifies the **real checked-in inventory**, attaches the DENIED report beside the xUnit governance evidence and retains Sprint34 external AI SDK gate, Sprint35 real browser HTTP security tests, protected health data DENY-ONLY, genuine isolated PostgreSQL negative tests and both Chromium built preview suites.

## Limits

Inventory accurately describes **unverified technical/external prerequisites**, NOT a binding clinical/legal list or exhaustive enterprise release checklist. The gate does not establish that a named partner, licence, patient consent, lawful medical service, insurer policy, network connectivity, paid member, national coverage or accredited clinician actually exists. It cannot compare real licensing evidence or execute Stage/QA/Production. It does not authorize data collection, payment, AI training/inference, an actual browser login or production AI promotion. Missing external partner/legal/medical/financial/security decisions remain open.

**Process:** PR Draft/Open/Unmerged. No Ready for Review, Merge, Stage, QA Gate, Release Approval, Production or claim of national readiness without explicit user instruction and real independently reviewed evidence.
