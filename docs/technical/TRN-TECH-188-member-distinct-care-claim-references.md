# TRN-TECH-188 — DRAFT / Member's action-specific unverified care-claim references

Date: 2026-10-20. Business TRN-BIZ-046 with TRN-BIZ-035/036/043 strictly separate; governed by existing TRN-TECH-115/167/184/185/187.

## Existing contract, concrete gap and change
Both Member follow-up domain actions already exist, but the Member Web QA previously reused one shared editable source field for a contact-note claim and a claimed clinical-source reference. They are independent, unverified concepts and cannot automatically attest one another. Center QA separated them in Sprint 178.

Keep existing PostgreSQL endpoints, schema, enums, CAS/revision, source GET and human-clicked commands. Provide distinct bounded `test:` inputs for contactNote and clinicalSource. Select exactly one according to action, including in the manually triggered guided step. Validate the selected action's required refs before allowing the button or issuing a POST; changing a field invalidates the earlier source snapshot. Keep the uncertain command's exact JSON/UUID unmodified, block other writes, reconcile with independently validated GET or human-clicked byte-identical replay. Render type-specific unverified labels in the existing event timeline. Nothing here verifies a physician, grants member data access/consent, schedules a visit, makes a clinical determination, initiates money movement or authorizes Production.

## Acceptance
Four real Chromium/Kestrel/isolated PostgreSQL tests: manual distinct refs, guided manual distinct refs, invalid contact input independent from valid clinical-source action, and lost clinical-source ACK resolved by GET-only with zero extra POST. Preserve all previous tests and full canonical 14-job exact-head CI; preliminary Code Review COMMENT only. PR stacked Draft/Open/Unmerged on Sprint 178; launch DENIED_BY_DEFAULT.
