# TRN-TECH-051 — Member-directed clinical-record sharing scope boundary

**Date:** 2026-10-11. **Status:** internal synthetic-only technical contract for Sprint 42. Based on accepted **TRN-BIZ-035** (member controls data sharing; licensed clinicians see only needed data for treatment), TRN-BIZ-036 (research/AI consent independent), TRN-BIZ-029 (sponsor payer has no health-record right), TRN-BIZ-041 (independent health core) and TRN-BIZ-046/047 (coordination and prevention do not imply access). Backlog TRN-EP-01/02/06/13. PR stacked on Sprint41 PR #48.

## Why this package

The Member Web `/member/records` route is currently only a generic disconnected placeholder. The product must distinguish **the member's own future self-read**, **limited treatment-need disclosure requested by an independently licensed clinician**, and **separate legal/emergency/secondary-use decisions**. A treatment role or a sponsor subscription must never become full record access. UI and internal test contract make the responsibilities explicit without leaking actual data, assuming an emergency exception, or introducing fabricated medical records.

## Internal .NET boundary

`SyntheticClinicalRecordDisclosureGate` accepts ONLY untrusted **`test:`** references and scalar claim flags, returns an **assessment of structural prerequisites only**, and is not an authorization engine. It is deliberately independent of `MemberSelfReadBoundary` and `SyntheticSecondaryUseEvidenceGate`. Valid input binds tenant, record-owner tenant, member, record-owner member and member-sharing subject; exact clinician/requester, treatment-service source, requested data category, current member-scoped category, claimed clinical-need reference and synthetic record source. Positive record/member-choice/clinical-need revisions are required without assuming their currency or validity.

One `ClinicianTreatmentNeed` request may be inspected only for a claimed matching clinician, explicit current member choice, independently scoped clinician/category/service, current withdrawal/restriction review, identity/session, professional qualification, service responsibility, service-specific need, separate lawful disclosure basis, data minimization, and auditable source/provenance. Each is `EvidenceCheck`; `Rejected` member sharing or withdrawal review fail closed, any missing or future/unknown status fails closed, and even all `Confirmed` **caller claims** give only `PreconditionsPresentOnly`. No data body or record source is opened; no authorization, token, referral, export or clinical decision is performed.

Requester categories Sponsor, Employer, NetworkAffiliate, Coordinator and ordinary Member do not inherit clinician access; self-read is explicitly delegated to the existing independent member-owned boundary. A purported `ResearchOrAiTraining` purpose is redirected to separate authorization, never care consent. A purported `EmergencyOrLegalException` cannot use this gate to establish exceptional disclosure; legal and clinical emergency exceptions have not been specified/approved.

## Member UI acceptance

The **existing** Persian RTL `/member/records` route displays three product lanes, each explicitly `اطلاعات موجود نیست`: future member-controlled self-read; exact-purpose clinician record request; legal/emergency exception requiring independently determined safeguards. A separate warning clarifies that treatment sharing ≠ research/AI consent. Buttons for viewing real record, sharing scope or reviewing access history are **disabled**, with no imaginary patient history, staff, consent, access grant, source, timestamp or count. Maintain no member identity, no API, no storage, HTTP CSP connect-src none and urgent-help messaging. Four real built Chromium tests cover lanes, role/consent separation, no private calls/storage and navigation history.

## Threat model / mandatory tests

Mismatch of member/record-owner/consent subject/tenant, claimant vs clinician, clinician scope, proposed treatment service or data category => DENY. Nonclinician sponsor/employer/network/coordinator, independent secondary research, emergency/legal exception => no clinical access. Each required EvidenceCheck is exercised both as missing and rejected; invalid future values and malformed/unscoped real IDs fail closed; no case contains real patient data. All twelve inherited CI suites including real PostgreSQL, member+center Chromium, external AI ban and comprehensive nationwide release blockers must remain green.

## Gated future integration

An actual identity and licensed provider directory, member's effective access/withdrawal policy and legal basis, clinical need approval, record categories/retention, medical/legal review, real health record source, trusted authorization server, auditable signed proof, emergency exception process and secure transport remain external prerequisites. No active login, actual record, consent, provider, workflow, FHIR API or medical information is invented. **Official nationwide launch DENIED_BY_DEFAULT**; no Ready/Merge/Stage/QA/Release/Production.
