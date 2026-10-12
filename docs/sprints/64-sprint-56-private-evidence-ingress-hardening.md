# TRN-SP-56 — Secure private evidence ingress and public-report isolation

**Date:** 2026-10-11. **Stack:** Sprint55 Draft PR #63. **Status:** Code/initial review candidate, NOT real evidence approval.

**Source chain:** accepted TRN-BIZ-028/030/035/036/040/041 → TRN-TECH-002 T-03/T-08 → 17-dimension TRN-TECH-045 DENY-ONLY → TRN-TECH-064 private custody → TRN-TECH-065 secure ingress → Sprint56.

**Value:** Close an identified unsafe check-then-open path race on the actual private external evidence intake mechanism before a human custodian ever uses it with a real file. Avoid fake partner/clinical read models and invented real medical/financial services.

**Implementation:** descriptor-rooted artifact read with no symlink following, positive evidence path identity checks, hardlink rejection, source manifest bounded non-following read, private-report destination fence, 8 new targeted Linux/POSIX regression scenarios including an in-read pathname swap. Keep 25 previous intake unit cases, national denied release gate, 28 public Git history security cases, and 12 inherited CI jobs. No confidential evidence enters the public repo or CI.

**Non-goals:** artifact issuer verification, legal review, credential storage, real IdP/billing/insurer/medical data, AI runtime, human review adjudication, official national release. The separate live trust contracts remain blocked. PR stays Draft/Open/unmerged and prior branches untouched.
