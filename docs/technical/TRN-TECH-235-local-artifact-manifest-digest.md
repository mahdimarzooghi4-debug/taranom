# TRN-TECH-235 — Bind Local Verification Output to Manifest Bytes

## Purpose

Make the local artifact verification report identify the exact manifest bytes used for its check without treating that digest as proof of origin or approval.

## Output contract

The local artifact CLI adds `manifest_sha256`, calculated from the same descriptor-pinned byte buffer that was parsed and verified. It also sets `manifest_authenticity_verified` to false on both success and denial responses. A denial before reading the file reports a null digest.

## Safety boundary

The SHA-256 value supports integrity comparison and audit correlation only. It does not authenticate who created the manifest, establish provenance or license rights, approve scientific quality, or authorize model loading, training, clinical use, or Production.

## Verification

Synthetic tests compare the report digest against exact manifest file bytes and assert the authenticity flag stays false. Existing local byte, duplicate-key, symlink, JSON-depth, and size checks remain in the same end-to-end CLI path.
