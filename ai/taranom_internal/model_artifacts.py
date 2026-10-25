"""Verify local model artifact manifests without loading or fetching weights."""
from __future__ import annotations

import errno
import hashlib
import os
from pathlib import Path, PurePosixPath
import re
import stat


class ArtifactRejected(ValueError):
    """Fail closed on untrusted or incomplete local model artifacts."""


_CANDIDATES = frozenset(("QWEN_QWEN2_5_7B_INSTRUCT",))
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
    if (type(name) is not str or not name or "\\" in name
            or ":" in name or "\x00" in name):
        raise ArtifactRejected("SAFE_RELATIVE_ARTIFACT_PATH_REQUIRED")
    if any(part in ("", ".", "..") for part in name.split("/")):
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


def _open_beneath(root_fd: int, parts: tuple[str, ...]) -> int:
    """Open a relative file through directory descriptors without following links."""
    nofollow = getattr(os, "O_NOFOLLOW", 0)
    directory = getattr(os, "O_DIRECTORY", 0)
    if not nofollow or not directory or os.open not in os.supports_dir_fd:
        raise OSError(errno.ENOTSUP, "descriptor-relative no-follow open unavailable")
    current_fd = os.dup(root_fd)
    try:
        for part in parts[:-1]:
            next_fd = os.open(
                part, os.O_RDONLY | directory | nofollow, dir_fd=current_fd
            )
            os.close(current_fd)
            current_fd = next_fd
        return os.open(
            parts[-1], os.O_RDONLY | nofollow | getattr(os, "O_NONBLOCK", 0),
            dir_fd=current_fd,
        )
    finally:
        os.close(current_fd)


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
    directory_flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        root_fd = os.open(root, directory_flags)
    except OSError as exc:
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED") from exc

    try:
        for name, expected_sha, expected_size in normalized:
            digest = hashlib.sha256()
            count = 0
            try:
                file_fd = _open_beneath(root_fd, PurePosixPath(name).parts)
            except FileNotFoundError as exc:
                raise ArtifactRejected("LOCAL_ARTIFACT_FILE_MISSING") from exc
            except OSError as exc:
                if exc.errno == errno.ELOOP:
                    raise ArtifactRejected("SYMLINK_ARTIFACT_PATH_DENIED") from exc
                if exc.errno == errno.ENOENT:
                    raise ArtifactRejected("LOCAL_ARTIFACT_FILE_MISSING") from exc
                raise ArtifactRejected("SAFE_LOCAL_ARTIFACT_OPEN_FAILED") from exc

            try:
                before = os.fstat(file_fd)
                if not stat.S_ISREG(before.st_mode):
                    raise ArtifactRejected("LOCAL_REGULAR_ARTIFACT_FILE_REQUIRED")
                if before.st_size != expected_size:
                    raise ArtifactRejected("ARTIFACT_SIZE_MISMATCH")
                while True:
                    chunk = os.read(file_fd, _CHUNK_SIZE)
                    if not chunk:
                        break
                    count += len(chunk)
                    if count > expected_size:
                        raise ArtifactRejected("ARTIFACT_SIZE_MISMATCH")
                    digest.update(chunk)
                after = os.fstat(file_fd)
                stable_before = (before.st_dev, before.st_ino, before.st_size,
                                 before.st_mtime_ns, before.st_ctime_ns)
                stable_after = (after.st_dev, after.st_ino, after.st_size,
                                after.st_mtime_ns, after.st_ctime_ns)
                if stable_after != stable_before or count != expected_size:
                    raise ArtifactRejected("ARTIFACT_CHANGED_DURING_READ")
            finally:
                os.close(file_fd)
            if digest.hexdigest() != expected_sha:
                raise ArtifactRejected("ARTIFACT_SHA256_MISMATCH")
    finally:
        os.close(root_fd)

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
