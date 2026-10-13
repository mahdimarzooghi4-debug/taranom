#!/usr/bin/env python3
"""TRN-TECH-064: offline custody check for PRIVATE external launch evidence.

No evidence or secrets should be committed to this public repository. Digest
integrity only; even success NEVER grants legal, clinical or release approval.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
from pathlib import Path, PurePosixPath
import re
import sys

MAX_MANIFEST = 512 * 1024
MAX_ARTIFACT = 20 * 1024 * 1024
SCOPE = "PRIVATE_EXTERNAL_EVIDENCE_INTAKE"
NATIONAL_SCOPE = "FIRST_COMPREHENSIVE_NATIONAL_OFFICIAL_LAUNCH"
REQUIRED = set([f"TRN-EP-{i:02d}" for i in range(1, 14)] + [
    "MARKET-PUBLIC", "MARKET-SUPPORT", "MARKET-CORPORATE", "NATIONAL-COVERAGE"
])
ROOT_FIELDS = {"schema_version", "scope", "source_inventory_sha256", "entries"}
ENTRY_FIELDS = {"dimension", "blocker", "artifact", "sha256", "custodian_ref", "review_state"}
DIGEST = re.compile(r"[0-9a-f]{64}\Z")
SAFE_SEGMENT = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,79}\Z")
CUSTODIAN_REF = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{3,79}\Z")


def _unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("DUPLICATE_JSON_KEY")
        result[key] = value
    return result


def _reject_constant(_value):
    raise ValueError("NONFINITE_JSON")


def _file_identity(st):
    return (st.st_dev, st.st_ino, st.st_size, st.st_mtime_ns, st.st_ctime_ns)


def _read_json(path: Path, *, owner_only: bool = False) -> tuple[dict, bytes]:
    """Read bounded, anchored JSON; do not follow a replaced symlink."""
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_size > MAX_MANIFEST:
            raise ValueError("INVALID_MANIFEST_FILE")
        if owner_only and (before.st_nlink != 1 or before.st_uid != os.getuid()
                or stat.S_IMODE(before.st_mode) & 0o077):
            raise ValueError("UNSAFE_PRIVATE_MANIFEST_FILE")
        with os.fdopen(os.dup(fd), "rb") as stream:
            raw = stream.read(MAX_MANIFEST + 1)
        after = os.fstat(fd)
        if (len(raw) > MAX_MANIFEST or _file_identity(before) != _file_identity(after)
                or _file_identity(after) != _file_identity(
                    os.stat(path, follow_symlinks=False))):
            raise ValueError("MANIFEST_CHANGED_DURING_READ")
    finally:
        os.close(fd)
    try:
        data = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique_pairs,
                          parse_constant=_reject_constant)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError("INVALID_JSON") from error
    if not isinstance(data, dict):
        raise ValueError("INVALID_JSON_ROOT")
    return data, raw


def _inside(path: Path, root: Path) -> bool:
    # Catch public lexical paths even when a symlink redirects outside, as well
    # as external-looking aliases that actually resolve into the public tree.
    return (path.absolute().is_relative_to(root.absolute())
            or path.resolve().is_relative_to(root.resolve()))


def _valid_artifact_name(name) -> bool:
    return (isinstance(name, str) and 0 < len(name) <= 200
            and not name.startswith("/") and "\\" not in name
            and all(SAFE_SEGMENT.fullmatch(p) and p not in {".", ".."}
                    for p in name.split("/"))
            and len(PurePosixPath(name).parts) == len(name.split("/")))


def _hash_private_file(evidence_root: Path, name: str, public_root: Path) -> str:
    """Open each component relative to an anchored directory FD (Linux/POSIX).

    No check-then-open path traversal; reject symlinks and hardlinks. The
    directory entries and file metadata must still identify the opened
    objects after hashing, even if an attacker moves a name during reading.
    """
    if not _valid_artifact_name(name):
        raise ValueError("UNSAFE_ARTIFACT_REFERENCE")
    if evidence_root.is_symlink() or not evidence_root.is_dir():
        raise ValueError("PRIVATE_EVIDENCE_ROOT_UNAVAILABLE")
    if _inside(evidence_root, public_root):
        raise ValueError("PUBLIC_REPOSITORY_EVIDENCE_FORBIDDEN")
    if not all(hasattr(os, flag) for flag in ("O_NOFOLLOW", "O_DIRECTORY")):
        raise ValueError("SECURE_DESCRIPTOR_OPEN_UNAVAILABLE")

    root_fd = os.open(evidence_root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    descriptors = [root_fd]
    chain = []
    try:
        root_before = os.fstat(root_fd)
        if not stat.S_ISDIR(root_before.st_mode):
            raise ValueError("PRIVATE_EVIDENCE_ROOT_UNAVAILABLE")
        parent_fd = root_fd
        for index, segment in enumerate(name.split("/")):
            is_last = index == len(name.split("/")) - 1
            # An explicit precheck supplies the accurate denial reason. The
            # O_NOFOLLOW at open also rejects a symlink installed afterwards.
            try:
                current = os.stat(segment, dir_fd=parent_fd, follow_symlinks=False)
            except FileNotFoundError as error:
                raise ValueError("ARTIFACT_UNAVAILABLE") from error
            if stat.S_ISLNK(current.st_mode):
                raise ValueError("SYMLINKED_ARTIFACT_FORBIDDEN")
            flags = os.O_RDONLY | os.O_NOFOLLOW
            flags |= os.O_NONBLOCK if is_last else os.O_DIRECTORY
            child_fd = os.open(segment, flags, dir_fd=parent_fd)
            descriptors.append(child_fd)
            chain.append((parent_fd, segment, child_fd))
            parent_fd = child_fd

        fd = parent_fd
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError("ARTIFACT_UNAVAILABLE")
        # Hardlinks can expose public tracked bytes outside the public pathname.
        if before.st_nlink != 1:
            raise ValueError("HARDLINKED_ARTIFACT_FORBIDDEN")
        if not 0 < before.st_size <= MAX_ARTIFACT:
            raise ValueError("ARTIFACT_SIZE_UNVERIFIABLE")

        digest = hashlib.sha256()
        read_length = 0
        with os.fdopen(os.dup(fd), "rb") as stream:
            while chunk := stream.read(65536):
                read_length += len(chunk)
                if read_length > MAX_ARTIFACT:
                    raise ValueError("ARTIFACT_SIZE_UNVERIFIABLE")
                digest.update(chunk)
        after = os.fstat(fd)
        if read_length != before.st_size or _file_identity(before) != _file_identity(after):
            raise ValueError("ARTIFACT_MUTATED_DURING_READ")

        root_after = os.stat(evidence_root, follow_symlinks=False)
        if _file_identity(root_before)[:2] != _file_identity(root_after)[:2]:
            raise ValueError("ARTIFACT_PATH_CHANGED")
        for parent, segment, child in chain:
            named = os.stat(segment, dir_fd=parent, follow_symlinks=False)
            opened = os.fstat(child)
            if (named.st_dev, named.st_ino) != (opened.st_dev, opened.st_ino):
                raise ValueError("ARTIFACT_PATH_CHANGED")
        return digest.hexdigest()
    finally:
        for fd in reversed(descriptors):
            os.close(fd)


def inspect(public_root: Path, evidence_root: Path, manifest: Path) -> dict:
    """Structural and actual file-byte custody check, never a readiness decision."""
    report = {
        "schema_version": 1,
        "scope": SCOPE,
        "status": "DENIED_INVALID_INTAKE",
        "release_allowed": False,
        "external_evidence_verified": False,
        "independent_review_observed": False,
        "submitted_files_integrity_checked": 0,
        "distinct_blocker_slots_with_files": 0,
        "errors": [],
    }
    errors = []
    try:
        inventory_path = public_root / "docs/governance/national-launch-blockers.json"
        inventory, inventory_bytes = _read_json(inventory_path)
        items = inventory.get("items")
        if (inventory.get("scope") != NATIONAL_SCOPE
                or inventory.get("release_admission") != "DENIED_BY_DEFAULT"
                or inventory.get("external_evidence_verified") is not False
                or not isinstance(items, list) or len(items) != 17):
            raise ValueError("SOURCE_BLOCKER_INVENTORY_INVALID")
        slots = {}
        for item in items:
            if (not isinstance(item, dict)
                    or item.get("id") not in REQUIRED
                    or item.get("id") in slots
                    or item.get("status") != "BLOCKED_UNVERIFIED"
                    or not isinstance(item.get("missing_evidence"), list)
                    or not item["missing_evidence"]
                    or len(item["missing_evidence"]) != len(set(item["missing_evidence"]))):
                raise ValueError("SOURCE_BLOCKER_INVENTORY_INVALID")
            slots[item["id"]] = set(item["missing_evidence"])
        if set(slots) != REQUIRED:
            raise ValueError("SOURCE_BLOCKER_INVENTORY_INVALID")

        # A public-repository alias to a private manifest must never become
        # an accepted reference, even when O_NOFOLLOW rejects the alias.
        if manifest.is_symlink() and _inside(manifest, public_root):
            raise ValueError("PRIVATE_MANIFEST_INSIDE_PUBLIC_REPO")
        private = not _inside(manifest, public_root)
        if private and (manifest.is_symlink()
                or any(p.is_symlink() for p in manifest.absolute().parents)):
            raise ValueError("UNSAFE_PRIVATE_MANIFEST_PATH")
        data, _ = _read_json(manifest, owner_only=private)
        if set(data) != ROOT_FIELDS or type(data.get("schema_version")) is not int or data["schema_version"] != 1:
            raise ValueError("INTAKE_SCHEMA_INVALID")
        if data.get("scope") != SCOPE or not isinstance(data.get("entries"), list):
            raise ValueError("INTAKE_SCOPE_OR_ENTRIES_INVALID")

        entries = data["entries"]
        if len(entries) > 200:
            raise ValueError("TOO_MANY_EVIDENCE_RECORDS")
        actual_inventory_hash = hashlib.sha256(inventory_bytes).hexdigest()
        if entries:
            if _inside(manifest, public_root):
                raise ValueError("PRIVATE_MANIFEST_INSIDE_PUBLIC_REPO")
            if not isinstance(data.get("source_inventory_sha256"), str) or (
                    data["source_inventory_sha256"] != actual_inventory_hash):
                raise ValueError("SOURCE_INVENTORY_DIGEST_MISMATCH")
            if _inside(evidence_root, public_root):
                raise ValueError("PRIVATE_EVIDENCE_ROOT_INSIDE_PUBLIC_REPO")
        elif data.get("source_inventory_sha256") is not None and (
                data["source_inventory_sha256"] != actual_inventory_hash):
            raise ValueError("SOURCE_INVENTORY_DIGEST_MISMATCH")

        seen = set()
        occupied = set()
        for entry in entries:
            if not isinstance(entry, dict) or set(entry) != ENTRY_FIELDS:
                raise ValueError("EVIDENCE_RECORD_SCHEMA_INVALID")
            dimension, blocker = entry["dimension"], entry["blocker"]
            if not isinstance(dimension, str) or dimension not in slots:
                raise ValueError("UNKNOWN_LAUNCH_DIMENSION")
            if not isinstance(blocker, str) or blocker not in slots[dimension]:
                raise ValueError("UNMAPPED_EXTERNAL_BLOCKER")
            if entry["review_state"] != "UNREVIEWED":
                raise ValueError("SELF_APPROVAL_FORBIDDEN")
            if (not isinstance(entry["custodian_ref"], str)
                    or not CUSTODIAN_REF.fullmatch(entry["custodian_ref"])):
                raise ValueError("INVALID_OPAQUE_CUSTODIAN_REF")
            digest = entry["sha256"]
            if not isinstance(digest, str) or not DIGEST.fullmatch(digest):
                raise ValueError("INVALID_ARTIFACT_DIGEST")
            filename = entry["artifact"]
            if not _valid_artifact_name(filename):
                raise ValueError("UNSAFE_ARTIFACT_REFERENCE")
            identity = (dimension, blocker, filename)
            if identity in seen:
                raise ValueError("DUPLICATE_EVIDENCE_RECORD")
            seen.add(identity)
            actual_digest = _hash_private_file(evidence_root, filename, public_root)
            if digest != actual_digest:
                raise ValueError("ARTIFACT_SHA256_MISMATCH")
            occupied.add((dimension, blocker))
            report["submitted_files_integrity_checked"] += 1
        report["distinct_blocker_slots_with_files"] = len(occupied)
        report["status"] = "INTACT_FILES_UNREVIEWED" if entries else "EMPTY_TEMPLATE_NO_EVIDENCE"
    except (OSError, ValueError, TypeError, KeyError) as error:
        # Never echo paths, private file names, custodian refs, digests or bytes.
        known = str(error) if isinstance(error, ValueError) else "INTAKE_UNVERIFIABLE"
        allowed = {
            "DUPLICATE_JSON_KEY", "NONFINITE_JSON", "INVALID_MANIFEST_FILE",
            "INVALID_JSON", "INVALID_JSON_ROOT", "UNSAFE_ARTIFACT_REFERENCE",
            "PRIVATE_EVIDENCE_ROOT_UNAVAILABLE", "SYMLINKED_ARTIFACT_FORBIDDEN",
            "ARTIFACT_UNAVAILABLE", "PUBLIC_REPOSITORY_EVIDENCE_FORBIDDEN",
            "ARTIFACT_SIZE_UNVERIFIABLE", "SOURCE_BLOCKER_INVENTORY_INVALID",
            "INTAKE_SCHEMA_INVALID", "INTAKE_SCOPE_OR_ENTRIES_INVALID",
            "TOO_MANY_EVIDENCE_RECORDS", "PRIVATE_MANIFEST_INSIDE_PUBLIC_REPO",
            "SOURCE_INVENTORY_DIGEST_MISMATCH",
            "PRIVATE_EVIDENCE_ROOT_INSIDE_PUBLIC_REPO", "EVIDENCE_RECORD_SCHEMA_INVALID",
            "UNKNOWN_LAUNCH_DIMENSION", "UNMAPPED_EXTERNAL_BLOCKER",
            "SELF_APPROVAL_FORBIDDEN", "INVALID_OPAQUE_CUSTODIAN_REF",
            "INVALID_ARTIFACT_DIGEST", "DUPLICATE_EVIDENCE_RECORD",
            "ARTIFACT_SHA256_MISMATCH", "MANIFEST_CHANGED_DURING_READ",
            "SECURE_DESCRIPTOR_OPEN_UNAVAILABLE", "ARTIFACT_PATH_CHANGED",
            "ARTIFACT_MUTATED_DURING_READ", "HARDLINKED_ARTIFACT_FORBIDDEN",
            "UNSAFE_PRIVATE_MANIFEST_FILE", "UNSAFE_PRIVATE_MANIFEST_PATH",
        }
        errors.append(known if known in allowed else "INTAKE_UNVERIFIABLE")
    report["errors"] = sorted(set(errors))
    return report



def _private_report_location(path: Path, public_root: Path) -> None:
    """Reject all public/aliased/unsafe private paths BEFORE reading evidence."""
    path = path.absolute()
    if (_inside(path, public_root) or path.is_symlink()
            or any(parent.is_symlink() for parent in path.parents)):
        raise ValueError("PRIVATE_REPORT_OUTSIDE_PUBLIC_REPO_REQUIRED")
    try:
        parent = os.stat(path.parent, follow_symlinks=False)
    except OSError as error:
        raise ValueError("PRIVATE_REPORT_DIRECTORY_UNAVAILABLE") from error
    if (not stat.S_ISDIR(parent.st_mode) or parent.st_uid != os.getuid()
            or stat.S_IMODE(parent.st_mode) & 0o077):
        raise ValueError("UNSAFE_PRIVATE_REPORT_DIRECTORY")
    if path.exists() or path.is_symlink():
        raise ValueError("PRIVATE_REPORT_ALREADY_EXISTS")


def _write_private_report(path: Path, public_root: Path, report: dict) -> None:
    """Exclusively create one private 0600 report, never overwrite a prior one.

    A pinned directory descriptor and per-inode cleanup prevent a symlink or
    rename race from redirecting output or deleting someone else's file.
    This does not protect a trusted operator from a privileged hostile host.
    """
    path = path.absolute()
    _private_report_location(path, public_root)
    directory_fd = None
    file_fd = None
    created = None
    try:
        directory_fd = os.open(path.parent,
                               os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        parent = os.fstat(directory_fd)
        named_parent = os.stat(path.parent, follow_symlinks=False)
        if (not stat.S_ISDIR(parent.st_mode) or parent.st_uid != os.getuid()
                or stat.S_IMODE(parent.st_mode) & 0o077
                or (parent.st_dev, parent.st_ino)
                   != (named_parent.st_dev, named_parent.st_ino)):
            raise ValueError("UNSAFE_PRIVATE_REPORT_DIRECTORY")
        file_fd = os.open(path.name, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                          os.O_NOFOLLOW, 0o600, dir_fd=directory_fd)
        opened = os.fstat(file_fd)
        created = (opened.st_dev, opened.st_ino)
        if (not stat.S_ISREG(opened.st_mode) or opened.st_nlink != 1
                or stat.S_IMODE(opened.st_mode) & 0o077):
            raise ValueError("UNSAFE_PRIVATE_REPORT_FILE")
        data = (json.dumps(report, sort_keys=True, indent=2) + "\n").encode("utf-8")
        with os.fdopen(os.dup(file_fd), "wb") as stream:
            stream.write(data)
            stream.flush()
        os.fsync(file_fd)
        named_file = os.stat(path.name, dir_fd=directory_fd, follow_symlinks=False)
        current_parent = os.stat(path.parent, follow_symlinks=False)
        if ((named_file.st_dev, named_file.st_ino) != created
                or (current_parent.st_dev, current_parent.st_ino)
                   != (parent.st_dev, parent.st_ino)):
            raise ValueError("PRIVATE_REPORT_PATH_CHANGED")
        os.fsync(directory_fd)
        created = None
    except (OSError, ValueError):
        if directory_fd is not None and created is not None:
            try:
                named = os.stat(path.name, dir_fd=directory_fd, follow_symlinks=False)
                if (named.st_dev, named.st_ino) == created:
                    os.unlink(path.name, dir_fd=directory_fd)
            except OSError:
                pass
        raise
    finally:
        if file_fd is not None:
            os.close(file_fd)
        if directory_fd is not None:
            os.close(directory_fd)


def _write_public_template_report(path: Path, report: dict) -> None:
    """Non-sensitive, empty-template CI output; still never clobber a file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL |
                 os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(os.dup(fd), "w", encoding="utf-8") as stream:
            stream.write(json.dumps(report, sort_keys=True, indent=2) + "\n")
        os.fsync(fd)
    finally:
        os.close(fd)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--evidence-root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args()
    public_root = args.root.resolve()
    manifest = args.manifest.absolute()
    private = not _inside(manifest, public_root)
    # Fail before reading actual private bytes when the report destination is
    # public, world-readable, symlinked, already occupied or an unsafe alias.
    try:
        if private:
            _private_report_location(args.report_json, public_root)
        report = inspect(public_root, args.evidence_root.absolute(), manifest)
        if private:
            _write_private_report(args.report_json, public_root, report)
        else:
            # Only the existing public EMPTY CI template may write under Git.
            # A private report is never inferred from a filename or count.
            if report["status"] != "EMPTY_TEMPLATE_NO_EVIDENCE":
                raise ValueError("PUBLIC_TEMPLATE_ONLY")
            _write_public_template_report(args.report_json, report)
    except (OSError, ValueError) as error:
        allowed = {
            "PRIVATE_REPORT_OUTSIDE_PUBLIC_REPO_REQUIRED",
            "PRIVATE_REPORT_DIRECTORY_UNAVAILABLE",
            "UNSAFE_PRIVATE_REPORT_DIRECTORY",
            "PRIVATE_REPORT_ALREADY_EXISTS",
            "UNSAFE_PRIVATE_REPORT_FILE",
            "PRIVATE_REPORT_PATH_CHANGED",
            "PUBLIC_TEMPLATE_ONLY",
        }
        label = str(error) if isinstance(error, ValueError) else ""
        print("DENIED: " + (label if label in allowed else
              "PRIVATE_REPORT_UNVERIFIABLE"), file=sys.stderr)
        return 1
    print("External-evidence custody: " + report["status"] +
          "; checked_files=" + str(report["submitted_files_integrity_checked"]) +
          "; release_allowed=false; independent_review=false.")
    if report["errors"]:
        print("DENIED: " + ", ".join(report["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
