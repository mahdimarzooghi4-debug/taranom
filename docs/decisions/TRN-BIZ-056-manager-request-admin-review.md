# TRN-BIZ-056 — Applicant-initiated center-manager request and Admin review

- **Status:** ACCEPTED Business rule, explicit product-owner instruction dated 2026-11-06: «مدیر مرکز درخواست میده و ادمین بررسی میکنه».
- **Parent:** TRN-BIZ-055 (only Admin defines canonical centers); TRN-BIZ-053/054 (center-scoped operations).
- A proposed manager applies under their own authenticated identity for an existing Admin-defined center. The claimant does **not** define a new center or specify another person's login subject.
- Only an authorized Taranom Admin may inspect the pending requests and record an accountable, independently authenticated review; applicants cannot review their own request.
- The applicant claim, even when reviewed, does not itself prove that the claimant is the organization's legitimate manager. Unavailable authoritative identity, representation, and delegation evidence stays explicitly missing.
- Admin review can document a refusal or an assessment awaiting independent identity/delegation verification. Neither status is a grant; the initial managerial grant must remain separately blocked until its trusted issuer-to-subject/organizational-authority contract and explicit Admin provisioning are established.
- No default/synthetic Admin or manager is bootstrapped. No clinical licensure, contracting, payment permission, provider publication, member discovery or medical access arises from a request or review.

Implementation detail: TRN-TECH-279. No Merge, Stage, QA gate, Release Approval or Production authorized.
