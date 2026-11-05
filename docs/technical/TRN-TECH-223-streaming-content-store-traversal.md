# TRN-TECH-223 — Streaming content-store traversal

## Contract

- Reconciliation consumes the top-level blob file enumeration lazily.
- Cancellation is checked before traversal and during reference/file processing.
- Findings remain sorted by identifier and finding kind, so report order is deterministic independently of filesystem enumeration order.

## Limits

The current API retains referenced digests and findings in memory. This reduces duplicate path-list allocation but does not provide a bounded-size report or streaming output.
