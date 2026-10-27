# Sprint 227 — Closed Local Model Artifact File Set

## Goal

Make offline local artifact verification account for every regular file under the selected artifact root.

## Delivered

- Walk the artifact tree through no-follow directory descriptors after validating manifest-declared bytes.
- Require exact equality between observed regular files and declared manifest paths.
- Deny undeclared files, symlinks, special files, excessive file/directory counts, and directory changes during the scan.
- Cover undeclared root/nested files and special files with synthetic tests.

## Safety boundary

This verifies local file-set completeness at check time. It does not authenticate sources or manifests, determine licensing or scientific validity, load model weights, or authorize training, clinical use, or Production.

## Delivery gates

Keep the PR Draft/Open/Unmerged. Exact-head CI and human review remain separate. No Stage, formal QA, Release, or Production.
