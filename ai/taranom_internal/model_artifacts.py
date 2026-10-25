"""Verify local model artifact manifests without loading or fetching weights."""
from __future__ import annotations

import hashlib
from pathlib import Path, PurePosixPath
import re


class ArtifactRejected(ValueError):
    """Fail closed on untrusted or incomplete local model artifacts."""


_CANDIDATES = frozenset(("QWEN_QWEN2_5_7B_INSTRUCT", "BIOPARS"))
_VERSION = re.compile(r"[A-Za-z0-9][A-Za-z0-9._+-]{0,127}\Z", re.ASCII)
_SHA256 = re.compile(r"[0-9a-f]{64}\Z", re.ASCII)
_MANIFEST_KEYS = frozenset(("schema_version", "candidate_ref", "artifact_version", "files"))
_FILE_KEYS = frozenset(("path", "sha256", "size_bytes"))
_MAX_FILES = 8192
_MAX_TOTAL_BYTES = 100 * 1024 * 1024 * 1024
_CHUNK_SIZE = 1024 * 1024


def _file_entry(raw: object) -> tuple[str, str, int]:
    if type(raw) is not dict or set(raw) != _FILE_KEYS:
        raise ArtifactRejected("EXACT_ARTIFACT_FILE_SCHEMA_REQUIRED")
    name = raw["path"]
    if type(name) is not str or not name or "\\" in name or "\x00" in name:
        raise ArtifactRejected("SAFE_RELATIVE_ARTIFACT_PATH_REQUIRED")
    relative = PurePosixPath(name)
    if relative.is_absolute() or any(part in ("", ".", "..") for part in relative.parts):
        raise ArtifactRejected("SAFE_RELATIVE_ARTIFACT_PATH_REQUIRED")
    digest = raw["sha256"]
    if type(digest) is not str or not _SHA256.fullmatch(digest):
        raise ArtifactRejected("LOWERCASE_SHA256_REQUIRED")
    size = raw["size_bytes"]
    if type(size) is not int or size <= 0:
        raise ArtifactRejected("POSITIVE_ARTIFACT_SIZE_REQUIRED")
    return relative.as_posix(), digest, size


def verify_local_model_manifest(manifest: object, artifact_root: str | Path) -> dict[str, object]:
    """Check declared bytes under a local root; this function never loads weights.

    The root must already exist. Network identifiers, path traversal, symlinks,
    missing files, empty files, size mismatches, and digest mismatches fail closed.
    """
    if type(manifest) is not dict or set(manifest) != _MANIFEST_KEYS:
        raise ArtifactRejected("EXACT_LOCAL_MODEL_MANIFEST_REQUIRED")
    if type(manifest["schema_version"]) is not int or manifest["schema_version"] != 1:
        raise ArtifactRejected("UNSUPPORTED_MODEL_MANIFEST_VERSION")
    candidate = manifest["candidate_ref"]
    if type(candidate) is not str or candidate not in _CANDIDATES:
        raise ArtifactRejected("UNAPPROVED_INTERNAL_CANDIDATE_REF")
    version = manifest["artifact_version"]
    if type(version) is not str or not _VERSION.fullmatch(version):
        raise ArtifactRejected("BOUNDED_ARTIFACT_VERSION_REQUIRED")
    entries = manifest["files"]
    if type(entries) is not list or not entries or len(entries) > _MAX_FILES:
        raise ArtifactRejected("BOUNDED_NONEMPTY_ARTIFACT_FILE_LIST_REQUIRED")
    normalized = [_file_entry(entry) for entry in entries]
    names = [entry[0] for entry in normalized]
    if len(set(names)) != len(names):
        raise ArtifactRejected("DUPLICATE_ARTIFACT_PATH")
    total_size = sum(entry[2] for entry in normalized)
    if total_size > _MAX_TOTAL_BYTES:
        raise ArtifactRejected("ARTIFACT_SIZE_LIMIT_EXCEEDED")

    root = Path(artifact_root)
    if root.is_symlink() or not root.is_dir():
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED")
    root = root.resolve(strict=True)
    for name, expected_sha, expected_size in normalized:
        target = root.joinpath(*PurePosixPath(name).parts)
        current = root
        for part in PurePosixPath(name).parts:
            current = current / part
            if current.is_symlink():
                raise ArtifactRejected("SYMLINK_ARTIFACT_PATH_DENIED")
        try:
            resolved = target.resolve(strict=True)
        except OSError as exc:
            raise ArtifactRejected("LOCAL_ARTIFACT_FILE_MISSING") from exc
        if not resolved.is_relative_to(root) or not resolved.is_file():
            raise ArtifactRejected("LOCAL_REGULAR_ARTIFACT_FILE_REQUIRED")
        if resolved.stat().st_size != expected_size:
            raise ArtifactRejected("ARTIFACT_SIZE_MISMATCH")
        digest = hashlib.sha256()
        with resolved.open("rb") as stream:
            for chunk in iter(lambda: stream.read(_CHUNK_SIZE), b""):
                digest.update(chunk)
        if digest.hexdigest() != expected_sha:
            raise ArtifactRejected("ARTIFACT_SHA256_MISMATCH")

    return {
        "status": "LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED",
        "candidate_ref": candidate,
        "artifact_version": version,
        "file_count": len(normalized),
        "total_size_bytes": total_size,
        "model_loaded": False,
        "network_used": False,
        "license_reviewed": False,
        "clinical_use_allowed": False,
        "training_allowed": False,
        "production_allowed": False,
    }
