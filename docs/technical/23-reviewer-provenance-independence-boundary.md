# TRN-TECH-023 — Reviewer Independence and Source Provenance Admission (Test Only)

**Date:** 2026-10-10. **Status:** internal synthetic-only admission contract; no actual authority, real professional verifier or human decision. Depends on Draft PR #20 Human Review Notes, Draft PR #19 frozen evidence and the prior unmerged technical chain. Business boundaries TRN-BIZ-043/044/053/054: one center may manage its own doctors, but affiliation and unverified submissions cannot grant clinical rights, approve medical qualification or establish a binding provider contract.

## Risk corrected

The previous review-note journal deliberately accepted a caller-asserted `ReviewerClaimRef` and a synthetic rationale reference, without a legitimate authenticated reviewer registry. A note persisted in PostgreSQL cannot establish independence, reviewer delegation, authoritative medical registration or traceable professional evidence. A reviewer who submitted the original claim or the subject physician could present a seemingly normal note. **This Sprint does not invent a reviewer role or approve those notes.** It defines a separate, defensive admission inspection with meaningful negative behavior and verified data lineage.

## Implemented test-only inspection

`PgReviewerProvenanceInterlock` is an INTERNAL helper in the existing non-packable synthetic Npgsql journal. An optional `IReviewerProvenanceSource` seam represents a future, independently trusted SERVER-side reviewer identity/source/delegation registry, but **no real implementation or trust anchor exists**. The default is disconnected and returns `AuthoritativeSourceUnavailable` before reading notes or source events.

When a TEST DOUBLE supplies synthetic `ReviewerProvenanceEvidence`, the inspector requires:
- exact synthetic request scope: center, subject, type, event revision and human-note UUID; no names, national IDs, clinical files, external IDs or claimed actual reviewers;
- test-supplied subject/session identity binding and reviewer identity mapping, current review delegation, current nonrevoked grant version, audit availability and independence-of-source signal, each separately fail-closed;
- exact center agreement between query and supplied source marker; **a marker match is not legal tenancy or reviewer authorization**;
- read the persisted PostgreSQL note history (verified against its paired accepted audit and hash) and the accepted evidence-intake history (also versioned and checked) for that exact center/subject/type/revision; require exactly one note whose request UUID matches and whose reviewer marker equals the test-supplied resolved reviewer;
- ensure the purported reviewer differs from BOTH original evidence submitter and the physician/representative who is the subject of the source claim. Equal identity is explicitly `ReviewerNotIndependent`, not a positive human review;
- compare the original source evidence reference with the test-supplied asserted origin marker; missing, contradictory or non-validated origins are rejected.

The **strongest positive result is `PreconditionsPresentOnly`**. A fully fake `ConfirmedByTestDouble` source **cannot** produce `Approved`, `Authorized`, `Verified`, `Qualified`, clinician activation, accepted legal contract, protected patient data, medical permission or a production model grant.

The recorded review note itself remains an untrusted input. This inspector only checks prerequisites before any future independently designed review decision. It neither mutates the previous note aggregate nor creates new approved human outcomes, user accounts, tokens, medical identity, external API, workflow assignments, threshold, approval taxonomy, or live UI.

## Acceptance and limits

29 new real PostgreSQL CI cases test default denial, unavailable/outage test source, all-synthetic-positive still nonauthorizing, review-submitter equality, subject self-review, note/issuer identity mismatch, center and source mismatch, current/stale/revoked delegation, current session, missing provenance, malformed identifiers, no note, and corrupted accepted evidence fail-closed. They join the previous 77 real PostgreSQL review/evidence tests in one inherited job with all 12 existing independent .NET, two Chromium and PostgreSQL regression jobs. Results must be reported only after exact SHA green.

**Unresolved:** legitimate IdP, signed session, current reviewer appointment/delegation registry, independent medical regulator/licensure source, professional verification standard, legal reviewer powers, reviewer recusal and conflict-of-interest policy, actual Taranom physician contract authority, appeal and human decision semantics, evidence privacy/provenance, production DB access/retention and RLS. This is not a full impartiality proof; checking identity equality is a *minimum negative guard*, not a complete COI policy. SQL owner can still alter hashes and DML triggers. Nothing is eligible for real professional approval.

Business/Technical/Sprint PRs remain Draft/Open and unmerged. No Ready/Merge/Stage/QA/Release/Production without explicit user approval and independent medical/legal/security Code Review.
