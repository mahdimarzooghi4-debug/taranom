# TRN-TECH-049 — Member-directed prevention, reviewed education and clinician-owned continuity of care

**Date:** 2026-10-11. **Stack:** Sprint39 Draft PR #46. **Accepted Business:** TRN-BIZ-047; cross-references TRN-BIZ-030/034/035/036/037/043/046. This Sprint is an internally testable contract and disconnected member interface, **not** publication of clinically reviewed medical education or activation of health services.

## Scope and separation

TRN-BIZ-047 establishes THREE related but independently accountable pathways: (1) Taranom presents reviewed, rights-cleared health education; (2) Taranom coordinates member-directed preventive service participation; and (3) ONLY a qualified medical professional/team may propose, begin or change a continuous clinical care plan. The member controls participation and lawful data sharing. A provider suggestion never means auto-enrollment, health-file permission, mandatory participation, prescription or diagnosis. An accepted care/program choice NEVER substitutes for independently governed research/AI-training consent (TRN-BIZ-036). No concrete education material, clinician, program, schedule, assessment threshold, scientific-review authority, content owner, medical instruction or partner is assumed to exist.

The `SyntheticPreventionParticipationGate` adds a **pure, internal, read-only C# domain seam** in `Taranom.MemberGovernance`, accepting only synthetic `test:` refs and existing `EvidenceCheck` markers. It has:
- `SyntheticPreventionTrack`: HealthEducation, PreventiveCoordination, LongitudinalClinicalCare;
- `SyntheticParticipationOrigin`: MemberRequested or QualifiedClinicianProposed (with strict exact member-or-clinician proposer binding);
- `SyntheticPreventionReviewPurpose`: InspectParticipationPrerequisites, InspectEducationalContentPrerequisites, StartOrChangeClinicalPlan, ReuseMemberDataForResearchOrAi;
- Exact synthetic member, consent subject, tenant, program and proposer refs; an explicitly separate claimed responsible clinician for clinical or clinician-proposed activity;
- Four independent common consent/identity/purpose/lawful-basis checks, plus separate education scientific review, publishing rights and content-version governance for educational track; separate professional qualification/responsibility for clinician proposals and clinical track, and distinct claimed human care-plan review for continuous clinical care;
- Deny-first decision semantics: invalid/cross-subject/impersonated proposer fails closed; explicitly rejected member choice prevents participation; separate data research/AI-purpose is NEVER accepted; starting or modifying a medical plan is NEVER allowed in this gate; missing or rejected evidence prevents even structural completeness. Future enum values also fail closed.
- Even **ALL** caller-claimed `Confirmed` evidence yields only `PreconditionsPresentOnly` — never a real clinician authorization, valid consent, published health content, activated program, clinical plan, permanent monitoring, research access or payment.

This is NOT a generic event journal, backend API, real consent ledger, licensed review, identity mapping or AI data collection. No patient data, credentials, external model/API or supposed clinical program is introduced.

## Member-facing product slice

A NEW, navigable `/member/prevention` route in the existing **built, disconnected Persian RTL Member Web** shows distinct education, nonclinical prevention coordination and clinician-led continuity cards. Each declares `شواهد معتبر موجود نیست`. Separate member participation and independent research consent warning; both membership/participation and clinician proposal buttons **disabled**, no material containing medical advice, no claimed verified clinician, no active program. An independent non-diagnostic emergency notice remains clearly visible.

4 added real Chromium cases: route/card roles/unknown status, consent and research distinction (including navigation to independent consent page), absence of assumed clinical plan, disabled buttons/emergency warning, no private API/network/browser storage, browser Back/Forward accessibility focus. One inherited regression extends its route list from nine to ten.

## Gates

Inherited **12 independent CI jobs** on Sprint40 Push and PR against Sprint39 branch, including Member xUnit, Center xUnit, isolated genuine PostgreSQL, protected center/clinical data DENY-ONLY, security of real Vite preview HTTP frame-ancestors/XFO, static external-AI runtime ban, national release registry DENIED_BY_DEFAULT and both signed-*nothing* local bundle SHA-256 verification. Tests and source review are technical preliminary checks only.

**Explicitly blocked:** real member identity, legally valid consent, PHR, reviewed medical education and content rights, licensed clinical team and longitudinal care, automatic AI risk screening, financial disbursement, clinical emergency dispatch, paid subscription, Stage/QA, independent medical/legal/privacy/security signoff, national official release. Do not treat a positive unit test as granted authority. PR Draft/Open/Unmerged, no merge/release/deploy.
