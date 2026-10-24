# TRN-TECH-141 — Sprint 132: Internal AI offline dataset + model + evaluation backend

**Product priority change:** return to substantive Backend/AI engineering instead of repeated Stage-only gating. Stacked Draft PR after Sprint 131. Domain: TRN-EP-11; architecture decisions TRN-BIZ-033/036/037/038/039 and TRN-TECH-005. **Internal Python-only executable**, no external service, real patients, AI model weights, training, diagnosis or Production. .NET still owns future operative governance and promotion.

## Shipped working capabilities

1. SQLite-backed local technical persistence, transaction-serialized revision writes (`BEGIN IMMEDIATE`), immutable tables enforced by BEFORE UPDATE/DELETE DB triggers. All tenant, version, request, artifact refs are mandatory `test:` and all hashes are uppercase SHA-256-shaped metadata. This is *not* a healthcare or authorized organization database.
2. `freeze_dataset`: builds an order-invariant, SHA-256 content-addressed manifest from synthetic metadata only. Both TRAINING and INDEPENDENT_EVALUATION partitions required, **no source overlap and no subject overlap**, explicit claimed consent and rights review refs. It stores no record/PHI or actual consent and therefore cannot mark data eligible for training. Tenant/version/idempotency concurrency guarded.
3. `register_model`: immutable versioned registry entry with claimed artifact digest, dataset ref and adapter ref. It **must** refer to a previously frozen same-tenant manifest; registering an adapter ref never loads, selects or authorizes inference.
4. `evaluate_synthetic`: computes genuine exact-decimal MAE/MSE from explicit test-only observed/predicted numeric *strings*, requiring exactly the held-out independent-evaluation source set of the bound versioned dataset. No training set leakage; rejects NaN/Infinity/overflow, stale/mismatched lineage and replay drift. Evidence is for **math/lineage regression only**: cannot evaluate medical quality, select a winner, promote, train or operate a model.
5. Read-only inventory API via local CLI provides per-tenant versions and fields `production_allowed=false`, `training_allowed=false`, `inference_supported=false`, `stage_admission=DENIED`. This is a backend control-plane foundation for future admin panel, NOT an authenticated production admin feature.

## Acceptance

24+ stdlib Python end-to-end tests using a genuine temporary SQLite database (not mocked): end-to-end persistence, idempotent replay and conflicts, immutable rows, tenant partitioning, training/eval leakage, artifact/dataset binding, exact metric calculation, rejection of malformed/non-synthetic claims, external runtime refusal, CLI and no Production activation. Every test is part of inherited mandatory 14-job full regression; existing PostgreSQL/Chromium/supply-chain/security tests remain intact.

## Deliberate unresolved contracts

Real OIDC/session and organizational admin permissions, lawful first-party data ingestion/withdrawal/retention, effective real clinical partition separation, model family/adapter/runtime, governed training trigger/cadence, actual independent evaluation/quality/clinical thresholds, real artifact integrity/signature, approved panel promotion, rollback/monitoring and Stage/Production environment are NOT selected or implied. Do not hardcode a model family, any threshold or false real evidence. Real Model Registry and the authenticated admin panel must bind to an approved future .NET backend API, after relevant contracts.

No Stage/QA/Release/Production or Merge; national `DENIED_BY_DEFAULT` remains.
