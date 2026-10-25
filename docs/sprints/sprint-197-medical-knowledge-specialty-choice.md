# Sprint 197 — Knowledge provenance and synthetic specialty-choice foundation

**Status: Implementation candidate, Draft PR; no Stage/QA Gate/Release/Production.**
**Owner decision:** help patient choose physician, final decision always patient's; first build structured scientific knowledge, then evaluated AI. Existing .NET domain authority and internal Python AI split preserved.

## Sprint acceptance contract
- Frozen, content-fingerprinted bibliography metadata only; source rights and scientific quality always unverified unless separately checked by qualified operators. No URL fetch, raw article copying, real patient or doctor identity.
- Synthetic, evidence-linked Persian test input shows deterministic unranked specialty and provider options with explicit "not clinical advice" authority flags.
- Unknown and ambiguous matches fail safe; missing source/review/registry, duplicates, spoofed permission and mismatched snapshots rejected.
- Existing complete Python AI test discovery and 14-job CI must remain green; independently inspect code; PR stays Draft/Open.

## Next Sprint candidates, dependent on evidence
Curated source ingestion and rights workflow; independent terminology and clinical guideline review; protected .NET physician directory and member routing UI; quantitative offline clinical evaluation before choosing any concrete internal model. None is authorized to make clinical decisions or to deploy.
