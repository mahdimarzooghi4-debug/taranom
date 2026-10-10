# TRN-TECH-014 — Center management web contract (Sprint 05)

Date: 2026-10-10. Status: disconnected shell and negative-browser-test implementation only. Business basis TRN-BIZ-053/054 in Draft PR #10 and isolated pure .NET center governance in Draft PR #11.

**One center panel with five routes**, NOT five panels: Overview; Departments; Doctors; Qualification Boundaries; Audit. One center may have several departments, including a chain hospital treated as one multi-department center. No independent network parent/administrator entity, cross-center management route or extra tenant selection.

The planned real actions of an authorized center manager are DefineDepartment, RecordDoctorAffiliation, AssignDoctorToDepartment and EndDoctorAffiliation. Until a trustworthy .NET server authenticates the **specific center manager**, pins current center/version, verifies rights and persists append-only audit, the action controls remain **non-mutating help dialogs with disabled submit**. No unofficial in-memory editing or ephemeral fake doctor data masquerading as a true record.

Physician affiliation `PendingIndependentVerification` in the pure reducer never means accepted clinician, professional license, health-data access, billing/insurance privileges, provider dispatch or wallet payment. The future real review/activation contract is out of scope and must not be guessed.

All five paths are browser-only; no backend endpoint, JWT claim, cookie, server trust root, authorization scope, row-level database grant or source identifier is defined here. No insurance/fund/finance or AI integrations.

Browser acceptance: every route safe disconnected; exact RTL, responsive navigation, route focus and Back; actions only open disabled dialog with Escape and focus restoration; browser spoofing via role/tenant/query cannot expose physician records or enable submit; no credentials, storage, private network or offsite requests; production Vite bundle is served, not HMR; no unrelated member-regression changes.

CI: independent Node22/TypeScript/Vite/Playwright job and the inherited five independent Sprint 01–04 tests; an exact-head green workflow is Code evidence but never a production approval. External provider credentials/contracts, independent code review and real security/privacy/clinical QA remain necessary.
