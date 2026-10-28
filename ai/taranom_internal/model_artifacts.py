"""Verify local model artifact manifests without loading or fetching weights."""
from __future__ import annotations

import errno
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import uuid


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
_MAX_MANIFEST_BYTES = 1024 * 1024
_MAX_MANIFEST_JSON_DEPTH = 32
_MAX_STORE_ENTRIES = 4096


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



def _stat_signature(value: os.stat_result) -> tuple[int, int, int, int]:
    return (value.st_dev, value.st_ino, value.st_mtime_ns, value.st_ctime_ns)


def _open_directory_beneath(root_fd: int, parts: tuple[str, ...]) -> int:
    current_fd = os.dup(root_fd)
    try:
        for part in parts:
            next_fd = os.open(
                part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                dir_fd=current_fd,
            )
            os.close(current_fd)
            current_fd = next_fd
        return current_fd
    except OSError:
        os.close(current_fd)
        raise


def _check_exact_artifact_file_set(
    root_fd: int, expected_files: set[str]
) -> None:
    """Reject unlisted or unsafe entries under the pinned artifact root."""
    if os.listdir not in os.supports_fd or os.stat not in os.supports_dir_fd:
        raise ArtifactRejected("SAFE_ARTIFACT_DIRECTORY_SCAN_NOT_SUPPORTED")

    observed_files: set[str] = set()
    directories: list[tuple[tuple[str, ...], tuple[int, int, int, int]]] = []

    def visit(directory_fd: int, parts: tuple[str, ...]) -> None:
        before = os.fstat(directory_fd)
        directories.append((parts, _stat_signature(before)))
        if len(directories) > _MAX_FILES:
            raise ArtifactRejected("ARTIFACT_DIRECTORY_COUNT_LIMIT_EXCEEDED")
        try:
            entries = os.listdir(directory_fd)
            for entry in entries:
                child_parts = parts + (entry,)
                try:
                    child_stat = os.stat(
                        entry, dir_fd=directory_fd, follow_symlinks=False
                    )
                except OSError as exc:
                    raise ArtifactRejected(
                        "ARTIFACT_DIRECTORY_CHANGED_DURING_INVENTORY"
                    ) from exc

                if stat.S_ISDIR(child_stat.st_mode):
                    try:
                        child_fd = os.open(
                            entry,
                            os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                            dir_fd=directory_fd,
                        )
                    except OSError as exc:
                        raise ArtifactRejected(
                            "UNSAFE_ARTIFACT_DIRECTORY_ENTRY"
                        ) from exc
                    try:
                        opened_stat = os.fstat(child_fd)
                        if _stat_signature(opened_stat)[:2] != (
                                child_stat.st_dev, child_stat.st_ino):
                            raise ArtifactRejected(
                                "ARTIFACT_DIRECTORY_CHANGED_DURING_INVENTORY"
                            )
                        visit(child_fd, child_parts)
                    finally:
                        os.close(child_fd)
                elif stat.S_ISREG(child_stat.st_mode):
                    observed_files.add(PurePosixPath(*child_parts).as_posix())
                    if len(observed_files) > _MAX_FILES:
                        raise ArtifactRejected("ARTIFACT_FILE_COUNT_LIMIT_EXCEEDED")
                else:
                    raise ArtifactRejected("UNSAFE_ARTIFACT_DIRECTORY_ENTRY")
        except OSError as exc:
            raise ArtifactRejected(
                "ARTIFACT_DIRECTORY_CHANGED_DURING_INVENTORY"
            ) from exc

        after = os.fstat(directory_fd)
        if _stat_signature(before) != _stat_signature(after):
            raise ArtifactRejected("ARTIFACT_DIRECTORY_CHANGED_DURING_INVENTORY")

    visit(root_fd, ())

    # Reopen every directory through no-follow descriptors after enumeration to
    # detect additions, removals, or directory replacement during the scan.
    for parts, signature in directories:
        try:
            directory_fd = _open_directory_beneath(root_fd, parts)
        except OSError as exc:
            raise ArtifactRejected(
                "ARTIFACT_DIRECTORY_CHANGED_DURING_INVENTORY"
            ) from exc
        try:
            if _stat_signature(os.fstat(directory_fd)) != signature:
                raise ArtifactRejected("ARTIFACT_DIRECTORY_CHANGED_DURING_INVENTORY")
        finally:
            os.close(directory_fd)

    unexpected = observed_files - expected_files
    if unexpected:
        raise ArtifactRejected("UNDECLARED_LOCAL_ARTIFACT_FILE")
    if observed_files != expected_files:
        raise ArtifactRejected("DECLARED_ARTIFACT_FILE_MISSING")


def _open_or_create_child_directory(parent_fd: int, name: str) -> int:
    try:
        os.mkdir(name, mode=0o700, dir_fd=parent_fd)
    except FileExistsError:
        try:
            info = os.stat(name, dir_fd=parent_fd, follow_symlinks=False)
        except OSError as exc:
            raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_PATH") from exc
        if not stat.S_ISDIR(info.st_mode):
            raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_PATH")
    try:
        return os.open(
            name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent_fd
        )
    except OSError as exc:
        raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_PATH") from exc


def _write_new_file(parent_fd: int, name: str, content: bytes) -> None:
    try:
        descriptor = os.open(
            name,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
            0o600,
            dir_fd=parent_fd,
        )
    except OSError as exc:
        raise ArtifactRejected("ARTIFACT_STORE_FILE_CREATE_FAILED") from exc
    try:
        remaining = memoryview(content)
        while remaining:
            written = os.write(descriptor, remaining)
            if written <= 0:
                raise ArtifactRejected("ARTIFACT_STORE_WRITE_FAILED")
            remaining = remaining[written:]
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def _remove_tree_at(parent_fd: int, name: str) -> None:
    """Remove one private temporary tree without following filesystem links."""
    child_fd = os.open(
        name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent_fd
    )
    try:
        for entry in os.listdir(child_fd):
            info = os.stat(entry, dir_fd=child_fd, follow_symlinks=False)
            if stat.S_ISDIR(info.st_mode):
                _remove_tree_at(child_fd, entry)
            else:
                os.unlink(entry, dir_fd=child_fd)
    finally:
        os.close(child_fd)
    os.rmdir(name, dir_fd=parent_fd)


def store_verified_local_model_artifact(
    manifest_path: str | Path,
    artifact_root: str | Path,
    store_root: str | Path,
    *,
    expected_manifest_sha256: str | None = None,
    expected_artifact_set_sha256: str | None = None,
) -> dict[str, object]:
    """Copy verified local files into a new content-addressed directory."""
    if os.rename not in os.supports_dir_fd:
        raise ArtifactRejected("SAFE_ATOMIC_ARTIFACT_STORE_NOT_SUPPORTED")
    verified = verify_local_model_artifact(
        manifest_path,
        artifact_root,
        expected_manifest_sha256=expected_manifest_sha256,
        expected_artifact_set_sha256=expected_artifact_set_sha256,
    )
    manifest_bytes = _read_local_model_manifest_bytes(manifest_path)
    if hashlib.sha256(manifest_bytes).hexdigest() != verified["manifest_sha256"]:
        raise ArtifactRejected("MODEL_MANIFEST_CHANGED_BEFORE_STORE")

    source = Path(artifact_root)
    if source.is_symlink() or not source.is_dir():
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED")
    source = source.resolve(strict=True)
    destination = Path(store_root)
    if destination.is_symlink() or not destination.is_dir():
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED")
    destination = destination.resolve(strict=True)

    directory_flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
    try:
        source_fd = os.open(source, directory_flags)
    except OSError as exc:
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED") from exc
    try:
        store_fd = os.open(destination, directory_flags)
    except OSError as exc:
        os.close(source_fd)
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED") from exc

    store_ref = str(verified["artifact_set_sha256"])
    temporary_name = ".tmp-" + uuid.uuid4().hex
    published = False
    temporary_created = False
    try:
        try:
            os.stat(store_ref, dir_fd=store_fd, follow_symlinks=False)
        except FileNotFoundError:
            pass
        except OSError as exc:
            raise ArtifactRejected("ARTIFACT_STORE_TARGET_CHECK_FAILED") from exc
        else:
            raise ArtifactRejected("ARTIFACT_STORE_TARGET_ALREADY_EXISTS")

        try:
            os.mkdir(temporary_name, mode=0o700, dir_fd=store_fd)
            temporary_created = True
            temporary_fd = os.open(
                temporary_name, directory_flags, dir_fd=store_fd
            )
        except OSError as exc:
            raise ArtifactRejected("ARTIFACT_STORE_TEMP_CREATE_FAILED") from exc

        try:
            payload_fd = _open_or_create_child_directory(temporary_fd, "payload")
            try:
                for item in verified["verified_files"]:
                    relative = PurePosixPath(str(item["path"]))
                    parts = relative.parts
                    source_file_fd = _open_beneath(source_fd, parts)
                    parent_fd = os.dup(payload_fd)
                    try:
                        for part in parts[:-1]:
                            next_fd = _open_or_create_child_directory(parent_fd, part)
                            os.close(parent_fd)
                            parent_fd = next_fd
                        target_fd = os.open(
                            parts[-1],
                            os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                            0o600,
                            dir_fd=parent_fd,
                        )
                        try:
                            before = os.fstat(source_file_fd)
                            if (not stat.S_ISREG(before.st_mode)
                                    or before.st_size != item["size_bytes"]):
                                raise ArtifactRejected("ARTIFACT_CHANGED_BEFORE_STORE")
                            copied = 0
                            digest = hashlib.sha256()
                            while True:
                                chunk = os.read(source_file_fd, _CHUNK_SIZE)
                                if not chunk:
                                    break
                                copied += len(chunk)
                                digest.update(chunk)
                                remaining = memoryview(chunk)
                                while remaining:
                                    written = os.write(target_fd, remaining)
                                    if written <= 0:
                                        raise ArtifactRejected("ARTIFACT_STORE_WRITE_FAILED")
                                    remaining = remaining[written:]
                            after = os.fstat(source_file_fd)
                            stable_before = (before.st_dev, before.st_ino, before.st_size,
                                             before.st_mtime_ns, before.st_ctime_ns)
                            stable_after = (after.st_dev, after.st_ino, after.st_size,
                                            after.st_mtime_ns, after.st_ctime_ns)
                            if (stable_before != stable_after
                                    or copied != item["size_bytes"]
                                    or digest.hexdigest() != item["sha256"]):
                                raise ArtifactRejected("ARTIFACT_CHANGED_DURING_STORE")
                            os.fsync(target_fd)
                        finally:
                            os.close(target_fd)
                    finally:
                        os.close(parent_fd)
                        os.close(source_file_fd)
                _check_exact_artifact_file_set(
                    source_fd,
                    {str(item["path"]) for item in verified["verified_files"]},
                )
                os.fsync(payload_fd)
            finally:
                os.close(payload_fd)

            _write_new_file(temporary_fd, "manifest.json", manifest_bytes)
            temporary_path = destination / temporary_name
            copy_verification = verify_local_model_artifact(
                temporary_path / "manifest.json",
                temporary_path / "payload",
                expected_manifest_sha256=str(verified["manifest_sha256"]),
                expected_artifact_set_sha256=store_ref,
            )
            receipt = {
                "status": "LOCAL_ARTIFACT_STORED_NOT_LOADED",
                "store_ref": store_ref,
                "verification": verified,
                "copy_verification": copy_verification,
                "model_loaded": False,
                "network_used": False,
                "manifest_authenticity_verified": False,
                "license_reviewed": False,
                "clinical_use_allowed": False,
                "training_allowed": False,
                "production_allowed": False,
            }
            receipt_bytes = json.dumps(
                receipt, sort_keys=True, separators=(",", ":"), ensure_ascii=False
            ).encode("utf-8")
            _write_new_file(temporary_fd, "verification.json", receipt_bytes)
            os.fsync(temporary_fd)

            try:
                os.rename(
                    temporary_name,
                    store_ref,
                    src_dir_fd=store_fd,
                    dst_dir_fd=store_fd,
                )
            except OSError as exc:
                raise ArtifactRejected("ARTIFACT_STORE_PUBLISH_FAILED") from exc
            published = True
            temporary_created = False
            os.fsync(store_fd)
        finally:
            os.close(temporary_fd)
    finally:
        if temporary_created and not published:
            try:
                _remove_tree_at(store_fd, temporary_name)
            except OSError:
                pass
        os.close(source_fd)
        os.close(store_fd)

    return {
        "status": "LOCAL_ARTIFACT_STORED_NOT_LOADED",
        "store_ref": store_ref,
        "store_created": True,
        "verification": verified,
        "copy_verification": copy_verification,
        "model_loaded": False,
        "network_used": False,
        "manifest_authenticity_verified": False,
        "license_reviewed": False,
        "clinical_use_allowed": False,
        "training_allowed": False,
        "production_allowed": False,
    }


def _check_manifest_json_depth(text: str) -> None:
    depth = 0
    in_string = False
    escaped = False
    for character in text:
        if in_string:
            if escaped:
                escaped = False
            elif character == "\\":
                escaped = True
            elif character == '"':
                in_string = False
            continue
        if character == '"':
            in_string = True
        elif character in "[{":
            depth += 1
            if depth > _MAX_MANIFEST_JSON_DEPTH:
                raise ArtifactRejected("MODEL_MANIFEST_NESTING_LIMIT")
        elif character in "]}":
            depth -= 1


def _unique_manifest_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ArtifactRejected("DUPLICATE_MODEL_MANIFEST_KEY")
        result[key] = value
    return result


def _reject_manifest_constant(value: str) -> object:
    raise ArtifactRejected("NONFINITE_MODEL_MANIFEST_NUMBER_DENIED")


def _read_local_model_manifest_descriptor(descriptor: int) -> bytes:
    """Read bounded strict JSON bytes from an already opened regular file."""
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ArtifactRejected("LOCAL_REGULAR_MANIFEST_FILE_REQUIRED")
        if before.st_size <= 0 or before.st_size > _MAX_MANIFEST_BYTES:
            raise ArtifactRejected("MODEL_MANIFEST_SIZE_LIMIT")
        chunks: list[bytes] = []
        count = 0
        while True:
            chunk = os.read(descriptor, min(_CHUNK_SIZE, _MAX_MANIFEST_BYTES + 1 - count))
            if not chunk:
                break
            chunks.append(chunk)
            count += len(chunk)
            if count > _MAX_MANIFEST_BYTES:
                raise ArtifactRejected("MODEL_MANIFEST_SIZE_LIMIT")
        after = os.fstat(descriptor)
        stable_before = (before.st_dev, before.st_ino, before.st_size,
                         before.st_mtime_ns, before.st_ctime_ns)
        stable_after = (after.st_dev, after.st_ino, after.st_size,
                        after.st_mtime_ns, after.st_ctime_ns)
        if stable_before != stable_after or count != before.st_size:
            raise ArtifactRejected("MODEL_MANIFEST_CHANGED_DURING_READ")
        return b"".join(chunks)
    finally:
        os.close(descriptor)


def _read_local_model_manifest_bytes(manifest_path: str | Path) -> bytes:
    """Read bounded manifest bytes through one pinned, no-follow regular file handle."""
    if not getattr(os, "O_NOFOLLOW", 0):
        raise ArtifactRejected("SAFE_MANIFEST_OPEN_NOT_SUPPORTED")
    try:
        descriptor = os.open(
            manifest_path,
            os.O_RDONLY | os.O_NOFOLLOW | getattr(os, "O_NONBLOCK", 0),
        )
    except OSError as exc:
        raise ArtifactRejected("SAFE_LOCAL_MANIFEST_OPEN_FAILED") from exc
    return _read_local_model_manifest_descriptor(descriptor)


def _read_local_model_manifest_at(directory_fd: int, name: str) -> bytes:
    """Read a manifest relative to a pinned directory without following links."""
    if not getattr(os, "O_NOFOLLOW", 0):
        raise ArtifactRejected("SAFE_MANIFEST_OPEN_NOT_SUPPORTED")
    try:
        descriptor = os.open(
            name,
            os.O_RDONLY | os.O_NOFOLLOW | getattr(os, "O_NONBLOCK", 0),
            dir_fd=directory_fd,
        )
    except OSError as exc:
        raise ArtifactRejected("SAFE_LOCAL_MANIFEST_OPEN_FAILED") from exc
    return _read_local_model_manifest_descriptor(descriptor)

def _parse_local_model_manifest(raw: bytes) -> object:
    try:
        text = raw.decode("utf-8", errors="strict")
        _check_manifest_json_depth(text)
        return json.loads(
            text,
            object_pairs_hook=_unique_manifest_object,
            parse_constant=_reject_manifest_constant,
        )
    except ArtifactRejected:
        raise
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError) as exc:
        raise ArtifactRejected("STRICT_MODEL_MANIFEST_JSON_REQUIRED") from exc


def load_local_model_manifest(manifest_path: str | Path) -> object:
    """Read and parse strict local JSON; digest calculation is returned by verifier."""
    return _parse_local_model_manifest(
        _read_local_model_manifest_bytes(manifest_path)
    )


def _validate_expected_sha256(value: object, reason: str) -> str | None:
    if value is None:
        return None
    if type(value) is not str or not _SHA256.fullmatch(value):
        raise ArtifactRejected(reason)
    return value


def verify_local_model_artifact(
    manifest_path: str | Path,
    artifact_root: str | Path,
    *,
    expected_manifest_sha256: str | None = None,
    expected_artifact_set_sha256: str | None = None,
) -> dict[str, object]:
    """Verify local bytes and optional comparison pins; never load weights."""
    expected_manifest = _validate_expected_sha256(
        expected_manifest_sha256, "LOWERCASE_MODEL_MANIFEST_SHA256_PIN_REQUIRED"
    )
    expected_artifact_set = _validate_expected_sha256(
        expected_artifact_set_sha256, "LOWERCASE_ARTIFACT_SET_SHA256_PIN_REQUIRED"
    )
    raw = _read_local_model_manifest_bytes(manifest_path)
    manifest_sha = hashlib.sha256(raw).hexdigest()
    if expected_manifest is not None and manifest_sha != expected_manifest:
        raise ArtifactRejected("MODEL_MANIFEST_PIN_MISMATCH")

    manifest = _parse_local_model_manifest(raw)
    result = verify_local_model_manifest(manifest, artifact_root)
    artifact_set_sha = str(result["artifact_set_sha256"])
    if expected_artifact_set is not None and artifact_set_sha != expected_artifact_set:
        raise ArtifactRejected("ARTIFACT_SET_PIN_MISMATCH")

    result["manifest_sha256"] = manifest_sha
    result["manifest_pin_supplied"] = expected_manifest is not None
    result["artifact_set_pin_supplied"] = expected_artifact_set is not None
    return result


def _artifact_set_sha256(
    candidate: str, version: str, verified_files: list[dict[str, object]]
) -> str:
    """Return a versioned, deterministic fingerprint for a verified file set."""
    digest = hashlib.sha256()
    digest.update(b"TRN-MODEL-ARTIFACT-SET\\x00v1\\x00")

    def add_field(value: bytes) -> None:
        digest.update(len(value).to_bytes(8, "big"))
        digest.update(value)

    add_field(candidate.encode("utf-8"))
    add_field(version.encode("utf-8"))
    for item in sorted(verified_files, key=lambda value: str(value["path"])):
        add_field(str(item["path"]).encode("utf-8"))
        digest.update(int(item["size_bytes"]).to_bytes(8, "big"))
        add_field(str(item["sha256"]).encode("ascii"))
    return digest.hexdigest()


def verify_local_model_store_entry(
    store_root: str | Path | int, store_ref: str
) -> dict[str, object]:
    """Read back a stored package from pinned directories and verify its contents."""
    if type(store_ref) is not str or not _SHA256.fullmatch(store_ref):
        raise ArtifactRejected("LOWERCASE_ARTIFACT_STORE_REF_REQUIRED")

    if type(store_root) is int:
        try:
            root_fd = os.dup(store_root)
            root_stat = os.fstat(root_fd)
            if not stat.S_ISDIR(root_stat.st_mode):
                os.close(root_fd)
                raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED")
        except OSError as exc:
            raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED") from exc
    else:
        root = Path(store_root)
        if root.is_symlink() or not root.is_dir():
            raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED")
        root = root.resolve(strict=True)
        directory_flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
        try:
            root_fd = os.open(root, directory_flags)
        except OSError as exc:
            raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED") from exc

    package_fd: int | None = None
    payload_fd: int | None = None
    try:
        try:
            package_stat = os.stat(store_ref, dir_fd=root_fd, follow_symlinks=False)
        except OSError as exc:
            raise ArtifactRejected("LOCAL_ARTIFACT_STORE_ENTRY_MISSING") from exc
        if not stat.S_ISDIR(package_stat.st_mode):
            raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_ENTRY")
        try:
            package_fd = os.open(store_ref, directory_flags, dir_fd=root_fd)
        except OSError as exc:
            raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_ENTRY") from exc

        package_before = os.fstat(package_fd)
        expected_names = {"payload", "manifest.json", "verification.json"}
        try:
            names = set(os.listdir(package_fd))
            for name, is_directory in (
                ("payload", True),
                ("manifest.json", False),
                ("verification.json", False),
            ):
                entry_stat = os.stat(
                    name, dir_fd=package_fd, follow_symlinks=False
                )
                valid_type = (
                    stat.S_ISDIR(entry_stat.st_mode)
                    if is_directory
                    else stat.S_ISREG(entry_stat.st_mode)
                )
                if not valid_type:
                    raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_ENTRY")
        except ArtifactRejected:
            raise
        except OSError as exc:
            raise ArtifactRejected("ARTIFACT_STORE_ENTRY_CHANGED") from exc
        if names != expected_names:
            raise ArtifactRejected("ARTIFACT_STORE_ENTRY_SCHEMA_INVALID")
        package_after_layout = os.fstat(package_fd)
        if _stat_signature(package_before) != _stat_signature(package_after_layout):
            raise ArtifactRejected("ARTIFACT_STORE_ENTRY_CHANGED")

        try:
            payload_fd = os.open("payload", directory_flags, dir_fd=package_fd)
        except OSError as exc:
            raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_ENTRY") from exc
        payload_before = os.fstat(payload_fd)
        if not stat.S_ISDIR(payload_before.st_mode):
            raise ArtifactRejected("UNSAFE_ARTIFACT_STORE_ENTRY")

        manifest_raw = _read_local_model_manifest_at(package_fd, "manifest.json")
        receipt_raw = _read_local_model_manifest_at(package_fd, "verification.json")
        manifest_sha = hashlib.sha256(manifest_raw).hexdigest()
        manifest = _parse_local_model_manifest(manifest_raw)
        verification = verify_local_model_manifest(manifest, payload_fd)
        artifact_set = str(verification["artifact_set_sha256"])
        if artifact_set != store_ref:
            raise ArtifactRejected("ARTIFACT_SET_PIN_MISMATCH")
        verification["manifest_sha256"] = manifest_sha
        verification["manifest_pin_supplied"] = False
        verification["artifact_set_pin_supplied"] = True

        copy_verification = dict(verification)
        copy_verification["manifest_pin_supplied"] = True
        copy_verification["artifact_set_pin_supplied"] = True

        receipt = _parse_local_model_manifest(receipt_raw)
        required_keys = {
            "status", "store_ref", "verification", "copy_verification",
            "model_loaded", "network_used", "manifest_authenticity_verified",
            "license_reviewed", "clinical_use_allowed", "training_allowed",
            "production_allowed",
        }
        if (type(receipt) is not dict or set(receipt) != required_keys
                or receipt["status"] != "LOCAL_ARTIFACT_STORED_NOT_LOADED"
                or receipt["store_ref"] != store_ref
                or any(receipt[key] is not False for key in (
                    "model_loaded", "network_used", "manifest_authenticity_verified",
                    "license_reviewed", "clinical_use_allowed", "training_allowed",
                    "production_allowed",
                ))):
            raise ArtifactRejected("ARTIFACT_STORE_RECEIPT_INVALID")

        source_record = receipt["verification"]
        copy_record = receipt["copy_verification"]
        pin_fields = {"manifest_pin_supplied", "artifact_set_pin_supplied"}
        if (type(source_record) is not dict or set(source_record) != set(verification)
                or type(copy_record) is not dict or set(copy_record) != set(copy_verification)
                or any(type(source_record[key]) is not bool for key in pin_fields)
                or any(type(copy_record[key]) is not bool for key in pin_fields)
                or any(source_record[key] not in (True, False) for key in pin_fields)
                or any(copy_record[key] not in (True, False) for key in pin_fields)
                or any(copy_record[key] != value
                       for key, value in copy_verification.items())
                or any(source_record[key] != value for key, value in verification.items()
                       if key not in pin_fields)):
            raise ArtifactRejected("ARTIFACT_STORE_RECEIPT_INVALID")

        payload_after = os.fstat(payload_fd)
        package_after_read = os.fstat(package_fd)
        try:
            current_package = os.stat(
                store_ref, dir_fd=root_fd, follow_symlinks=False
            )
        except OSError as exc:
            raise ArtifactRejected("ARTIFACT_STORE_ENTRY_CHANGED") from exc
        if (not stat.S_ISDIR(current_package.st_mode)
                or _stat_signature(package_before) != _stat_signature(current_package)
                or _stat_signature(package_before) != _stat_signature(package_after_read)
                or _stat_signature(payload_before) != _stat_signature(payload_after)):
            raise ArtifactRejected("ARTIFACT_STORE_ENTRY_CHANGED")
    finally:
        if payload_fd is not None:
            os.close(payload_fd)
        if package_fd is not None:
            os.close(package_fd)
        os.close(root_fd)

    return {
        "status": "LOCAL_ARTIFACT_STORE_ENTRY_VERIFIED_NOT_LOADED",
        "store_ref": store_ref,
        "stored_content_verified": True,
        "receipt_consistency_verified": True,
        "verification": verification,
        "copy_verification": copy_verification,
        "model_loaded": False,
        "network_used": False,
        "manifest_authenticity_verified": False,
        "license_reviewed": False,
        "clinical_use_allowed": False,
        "training_allowed": False,
        "production_allowed": False,
    }

def list_local_model_store_entries(store_root: str | Path) -> dict[str, object]:
    """Return a read-only inventory and verify each content-addressed entry."""
    root = Path(store_root)
    if root.is_symlink() or not root.is_dir():
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED")
    root = root.resolve(strict=True)
    directory_flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
    try:
        root_fd = os.open(root, directory_flags)
    except OSError as exc:
        raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED") from exc

    try:
        before = os.fstat(root_fd)
        try:
            names = sorted(os.listdir(root_fd))
        except OSError as exc:
            raise ArtifactRejected("ARTIFACT_STORE_INVENTORY_READ_FAILED") from exc
        if len(names) > _MAX_STORE_ENTRIES:
            raise ArtifactRejected("ARTIFACT_STORE_ENTRY_LIMIT_EXCEEDED")

        entries: list[dict[str, object]] = []
        for name in names:
            if not _SHA256.fullmatch(name):
                entries.append({
                    "store_ref": name,
                    "status": "DENIED",
                    "error": "LOWERCASE_ARTIFACT_STORE_REF_REQUIRED",
                    "stored_content_verified": False,
                    "receipt_consistency_verified": False,
                })
                continue
            try:
                verified = verify_local_model_store_entry(root_fd, name)
                verification = verified["verification"]
                entries.append({
                    "store_ref": name,
                    "status": verified["status"],
                    "candidate_ref": verification["candidate_ref"],
                    "artifact_version": verification["artifact_version"],
                    "artifact_set_sha256": verification["artifact_set_sha256"],
                    "stored_content_verified": verified["stored_content_verified"],
                    "receipt_consistency_verified": verified["receipt_consistency_verified"],
                })
            except (ArtifactRejected, OSError, ValueError) as exc:
                entries.append({
                    "store_ref": name,
                    "status": "DENIED",
                    "error": str(exc),
                    "stored_content_verified": False,
                    "receipt_consistency_verified": False,
                })

        after = os.fstat(root_fd)
        try:
            current_root = os.stat(root, follow_symlinks=False)
        except OSError as exc:
            raise ArtifactRejected("ARTIFACT_STORE_INVENTORY_CHANGED") from exc
        if (not stat.S_ISDIR(current_root.st_mode)
                or _stat_signature(before) != _stat_signature(after)
                or _stat_signature(before) != _stat_signature(current_root)):
            raise ArtifactRejected("ARTIFACT_STORE_INVENTORY_CHANGED")
    finally:
        os.close(root_fd)

    return {
        "status": "LOCAL_ARTIFACT_STORE_INVENTORY_READ_ONLY",
        "entry_count": len(entries),
        "entries": entries,
        "model_loaded": False,
        "network_used": False,
        "manifest_authenticity_verified": False,
        "license_reviewed": False,
        "clinical_use_allowed": False,
        "training_allowed": False,
        "production_allowed": False,
    }

def verify_local_model_manifest(manifest: object, artifact_root: str | Path | int) -> dict[str, object]:
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

    if type(artifact_root) is int:
        try:
            root_fd = os.dup(artifact_root)
            root_stat = os.fstat(root_fd)
            if not stat.S_ISDIR(root_stat.st_mode):
                os.close(root_fd)
                raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED")
        except OSError as exc:
            raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED") from exc
    else:
        root = Path(artifact_root)
        if root.is_symlink() or not root.is_dir():
            raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED")
        root = root.resolve(strict=True)
        directory_flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0) | getattr(os, "O_NOFOLLOW", 0)
        try:
            root_fd = os.open(root, directory_flags)
        except OSError as exc:
            raise ArtifactRejected("EXISTING_LOCAL_ARTIFACT_ROOT_REQUIRED") from exc

    verified_files: list[dict[str, object]] = []
    try:
        for name, expected_sha, expected_size in normalized:
            digest = hashlib.sha256()
            count = 0
            try:
                file_fd = _open_beneath(root_fd, PurePosixPath(name).parts)
            except FileNotFoundError as exc:
                raise ArtifactRejected("LOCAL_ARTIFACT_FILE_MISSING") from exc
            except OSError as exc:
                if exc.errno in (errno.ELOOP, errno.ENOTDIR):
                    raise ArtifactRejected("UNSAFE_ARTIFACT_PATH_COMPONENT_DENIED") from exc
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
            actual_sha = digest.hexdigest()
            if actual_sha != expected_sha:
                raise ArtifactRejected("ARTIFACT_SHA256_MISMATCH")
            verified_files.append({
                "path": name,
                "sha256": actual_sha,
                "size_bytes": count,
            })
        _check_exact_artifact_file_set(root_fd, set(names))
    finally:
        os.close(root_fd)

    return {
        "status": "LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED",
        "candidate_ref": candidate,
        "artifact_version": version,
        "file_count": len(normalized),
        "total_size_bytes": total_size,
        "verified_files": verified_files,
        "artifact_set_sha256": _artifact_set_sha256(
            candidate, version, verified_files
        ),
        "model_loaded": False,
        "network_used": False,
        "manifest_authenticity_verified": False,
        "license_reviewed": False,
        "clinical_use_allowed": False,
        "training_allowed": False,
        "production_allowed": False,
    }
