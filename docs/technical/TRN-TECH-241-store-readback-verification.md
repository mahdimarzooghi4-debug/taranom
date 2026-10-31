# TRN-TECH-241 — Read-Back Verification Before Artifact Publication

## Purpose

Verify bytes from the temporary store copy itself before making the content-addressed directory visible.

## Contract

After the payload and exact manifest are written into the private temporary directory, the store operation reopens the copied manifest and payload through the existing strict local verifier. It supplies the original manifest SHA-256 and artifact-set fingerprint as comparison pins. Publication proceeds only when the read-back succeeds. The stored receipt records both source verification and copy read-back verification.

## Safety boundary

Read-back proves that the bytes visible in the temporary local copy matched the expected local manifest and fingerprint at verification time. It does not authenticate the original source or receipt, establish rights or scientific approval, load weights, or grant training, clinical, or Production permission.

## Verification

Synthetic store API and CLI tests assert that the read-back fingerprint and per-file inventory match source verification before atomic publication.
