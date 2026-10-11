# TRN-TECH-054 — Synthetic provider-quality oversight and incident handoff lineage

**Recorded:** 2026-10-11. **Status:** INTERNAL synthetic-only technical contract, not medical, provider, or legal operations. **Base:** Sprint44 PR #51. **Business:** TRN-BIZ-043 direct/qualified-network pathways, TRN-BIZ-044 Taranom common standards and independent network control in contract, TRN-BIZ-045 member-facing unified complaints with separate professional authority, TRN-BIZ-034 emergency duties, TRN-BIZ-035 patient privacy and TRN-BIZ-009 payee contractual limitation. Backlog TRN-EP-04/10/12.

## Problem / existing contracts preserved

Sprint39 `SyntheticProviderNetworkEvidenceGate` already separates direct vs qualified-network provider and medical-credit payee evidence: this Sprint **does not duplicate it or allow a quality note to activate it**. Sprint30 `SyntheticSupportIntake` already models a distinct member complaint/insurer dispute intake; Sprint45 **does not reassign medical blame or merge that independent member case into provider oversight**. No provider registry, actual licensure, policy-grade quality metric, approval, case authority, clinical referral, evidence rights, insurer, payment or actual member record exists.

## Exact implemented boundary

New `SyntheticProviderQualityTrail` is an **internal pure immutable append-only technical evidence trace** within existing `Taranom.ClinicalCenterGovernance`, not a persistence API or clinical workflow. Four precisely non-authorizing actions:

1. `RecordUnverifiedQualityScope`: opens a test-only case with exact tenant, provider, service and provider relationship: `DirectContract` has NO partner network, `QualifiedPartnerNetwork` requires its own distinct network ref.
2. `RecordUnverifiedTaranomStandardNote`: records a claimed Taranom quality-oversight paper reference; neither policy adoption nor accreditation results.
3. `RecordUnverifiedNetworkProviderControlNote`: available only on a partner-network case, records a claimed provider-control paper reference, **not** proof of actual delegated legal authority, Taranom approval or current licence.
4. `RecordUnverifiedIncidentHandoffReference`: records an unverified reference to a proposed specialist medical-incident authority with opaque evidence/incident markers. It DOES NOT transmit a notice, triage clinical risk, determine fault, complete an official referral or alter a member complaint. Emergency help cannot await this administrative trace.

All fields are bounded `test:` markers; UUID request and original-scope request, expected revision, exact case/tenant/provider/service/path/network binding, actor and evidence refs on each note, and incident+recipient claims only on incident reference. Command receipts are NOT genuine authenticated actors. Replay exact input is idempotent even after later notes; changed same UUID conflicts, stale CAS refuses, cross-member/tenant/path/service/network substitution refuses, no note before original, no reopened scope. No patient ID, medical text, medical licence, clinical data, score, SLA, fault judgment, sanction, compensation or money can be introduced. The read model returns only the original synthetic scope, revision and **counts of claimed** note types; no substantive approval.

## Center Web and verification

On disconnected Persian RTL `/center/qualifications`, add an auditable responsibility explanation for 3 separate streams: Taranom oversight of uniform quality policies, partner network's bounded provider controls, and independent specialist medical-incident response; all **unavailable**. Member support retains its separate two-topic complaints paths. All controls disabled; no fictitious provider, rank, response/incident case or deadlines, no private data/API/storage and CSP remains `connect-src 'none'`. Three or four built Chromium cases for all three streams, no implied auto-suspension, privacy/emergency help, and browser history/focus. Negative .NET tests cover scope, replay/idempotency, stale, invalid, wrong network, actor/evidence contamination, incident handoff not real escalation. Preserve 12 inherited CI jobs, including actual PostgreSQL tests and national launch DENIED.

## Real launch blockers

National independent legal licensing verification, service-specific professional credentials, real partner delegated controls/contracts, clinical complaint authority, urgency triage, safeguarding, consent/record handling, Taranom standards and actual policy criteria, escalation and duty-to-report contracts, care safety and release governance are still OPEN. No medical emergency workflow/notification is created by any synthetic case. **No Ready/Merge/Stage/QA/Release/Production**.
