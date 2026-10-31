# TRN-TECH-225 — Center Queue Filter Reset

## Contract

The synthetic center queue offers a reset action when either optional filter is populated. Reset clears both filter inputs, drops any displayed prior page, and reloads the first page for the current center without filter parameters.

The response is still validated against the requested center, normalized empty filters, cursor, and deny-only flags before rendering.

## Verification

A Chromium test seeds one matching and one non-matching synthetic request, reads the filtered page, clears filters, and verifies both requests appear on the reloaded first page and both inputs are empty.

## Limits

The worklist remains synthetic and read-only. It does not authenticate a center, verify professional credentials or slot availability, or enable appointment acceptance, booking, or clinical actions.
