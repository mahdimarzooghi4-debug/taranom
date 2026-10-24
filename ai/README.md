# Taranom self-hosted AI technical foundation (TRN-TECH-141)

- `ai/taranom_internal/registry.py`: executable **local offline** and **persistent** SQLite registry for synthetic-only manifest versions, model artifact-metadata versions, and independent-partition synthetic evaluation. No model binaries, health records, external AI calls or training.
- `ai/taranom_internal/cli.py`: local operator/CI CLI; output is a typed read model designed for a future authenticated admin control-plane, **not** an admin portal with real credentials. No HTTP interface.
- `TARANOM_AI_OFFLINE_TEST_ONLY=CI_EPHEMERAL_ONLY` must be explicit and DB path must be absolute, SQLite, under the OS temporary directory.
- Run from repo root: `python3 -m ai.taranom_internal.cli --db /tmp/my-test/registry.sqlite3 init`. Read: `python3 -m ai.taranom_internal.cli --db /tmp/my-test/registry.sqlite3 inventory --tenant test:tenant-a`. Writes require exact JSON schemas in `tests/ai/test_offline_registry.py`.

The module is a **working engineering foundation**, not real model training or valid medical evaluation. `test:` claimed consent/right refs are unverified metadata. Artifacts are SHA-shaped claims only. No real Model Registry authorization, ONNX/runtime adapter, legal consent, clinical dataset, production API, actual admin permissions, evaluation thresholds, committee or final human approval is implemented; none is inferred. Positive tests never change `training_allowed=false`, `production_allowed=false` or `inference_supported=false`.

A compatible real model family requires a separately contracted local Runtime/Adapter, security/clinical evaluation and human-controlled admin promotion. Store permanent state in authorized owned operational storage only after database/runtime review; this temporary SQLite is never a production database.
