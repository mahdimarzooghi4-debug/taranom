# TRN-TECH-151 — Sprint 142: deterministic fault and recovery of synthetic internal AI worker

## Real executed scope
This sprint adds an independent nondeployable .NET executable fault probe that drives the **same** internal child-process lifecycle used by the Kestrel demo gateway. The adversarial process is a Python one-shot local fixture, invoked directly from the probe; **HTTP callers cannot select any fault mode, executable, key, output or operation**.

- The shared process runner enforces test-mode admission, exact 32-byte ephemeral key, bounded request (1024 bytes), bounded stdout (2048 bytes) and stderr (1024 bytes), both redirected and drained concurrently without logging, real child exit verification, full strict HMAC/lineage verification and fail-closed exceptions.
- A deadline `<=4s` covers stdin, stdout, stderr and process exit. The host request-aborted token is linked to the deadline. On failure/cancellation, the subprocess tree is killed and a best-effort bounded reap is attempted. No retry, no fallback synthetic success and no trained model invocation.
- The fault probe actually spawns a Python process for successful, fragmented, premature exit, empty reply, invalid JSON/UTF-8, oversize stdout, leaked stderr, oversize stderr, invalid HMAC, wrong tenant, appended content, valid reply with nonzero exit, hanging before reply, hanging after reply and recovery after each error. Bad stdout and stderr contents never appear in HTTP or logs. Invalid/missing program assumptions fail closed.
- Existing Kestrel→Python→SQLite HTTP and real PostgreSQL, Chromium, recovery and all 14 full regression gates remain intact. The new probe is mandatory in the `ephemeral-center-workflow-full-stack` job; no existing check is removed.

## Non-goals and unresolved authority
Test-only process health and IPC controls do **not** select the Production topology, model/trainer, resource quotas, Linux sandbox, OIDC/service identity, lawful first-party dataset, medical or technical committee members, or independent human Promotion authority. Nothing writes to a clinical/financial operational table, loads model weights, enables inference or launches an external AI API. Stage/QA/Release/Production and national rollout remain `DENIED_BY_DEFAULT`, and Draft PRs must remain Open/Unmerged.
