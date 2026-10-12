#!/usr/bin/env python3
"""TRN-TECH-079: private batch evidence preparation; NEVER an approval.

No genuine private inputs or outputs may enter the public repository or CI.
Actual file bytes are hashed by the existing descriptor-pinned custody checker.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import stat
import sys

import check_private_external_evidence as custody

REQUEST_SCOPE = "PRIVATE_EXTERNAL_EVIDENCE_BATCH_REQUEST"
REPORT_SCOPE = "PRIVATE_EXTERNAL_EVIDENCE_GAP_WORKBENCH"
MAX_ENTRIES = 200
REQUEST_ENTRY_FIELDS = {"dimension", "blocker", "artifact", "custodian_ref"}


class BatchError(Exception):
    pass


def _deny(code: str) -> None:
    raise BatchError(code)


def _private_path(path: Path, root: Path, *, output: bool) -> Path:
    path = path.absolute()
    if custody._inside(path, root) or path.is_symlink() or any(
        parent.is_symlink() for parent in path.parents
    ):
        _deny("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
    if output:
        try:
            parent = path.parent.stat()
        except OSError:
            _deny("PRIVATE_OUTPUT_DIRECTORY_UNAVAILABLE")
        if (not stat.S_ISDIR(parent.st_mode) or parent.st_uid != os.getuid()
                or stat.S_IMODE(parent.st_mode) & 0o077):
            _deny("UNSAFE_PRIVATE_OUTPUT_DIRECTORY")
        if path.exists() or path.is_symlink():
            _deny("PRIVATE_OUTPUT_ALREADY_EXISTS")
    return path


def _read_request(path: Path, root: Path) -> dict:
    path = _private_path(path, root, output=False)
    fd = None
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        before = os.fstat(fd)
        if (not stat.S_ISREG(before.st_mode) or before.st_nlink != 1
                or before.st_uid != os.getuid()
                or stat.S_IMODE(before.st_mode) & 0o077
                or not 0 < before.st_size <= custody.MAX_MANIFEST):
            _deny("UNSAFE_PRIVATE_REQUEST_FILE")
        with os.fdopen(os.dup(fd), "rb") as stream:
            raw = stream.read(custody.MAX_MANIFEST + 1)
        after = os.fstat(fd)
        named = os.stat(path, follow_symlinks=False)
        if (len(raw) > custody.MAX_MANIFEST
                or custody._file_identity(before) != custody._file_identity(after)
                or (named.st_dev, named.st_ino) != (after.st_dev, after.st_ino)):
            _deny("PRIVATE_REQUEST_CHANGED_DURING_READ")
        data = json.loads(raw.decode("utf-8"),
                          object_pairs_hook=custody._unique_pairs,
                          parse_constant=custody._reject_constant)
        if not isinstance(data, dict):
            _deny("INVALID_PRIVATE_REQUEST_SCHEMA")
        return data
    except BatchError:
        raise
    except (OSError, ValueError, UnicodeError, TypeError, OverflowError):
        _deny("PRIVATE_REQUEST_UNVERIFIABLE")
    finally:
        if fd is not None:
            os.close(fd)


def _source_inventory(root: Path) -> tuple[dict, bytes]:
    try:
        inventory, raw = custody._read_json(
            root / "docs/governance/national-launch-blockers.json")
        items = inventory.get("items")
        if (type(inventory.get("schema_version")) is not int
                or inventory["schema_version"] != 1
                or inventory.get("scope") != custody.NATIONAL_SCOPE
                or inventory.get("release_admission") != "DENIED_BY_DEFAULT"
                or inventory.get("external_evidence_verified") is not False
                or inventory.get("real_release_approval_observed") is not False
                or not isinstance(items, list) or len(items) != 17):
            _deny("SOURCE_INVENTORY_UNVERIFIABLE")
        slots = {}
        for item in items:
            if not isinstance(item, dict):
                _deny("SOURCE_INVENTORY_UNVERIFIABLE")
            ident = item.get("id")
            missing = item.get("missing_evidence")
            if (not isinstance(ident, str) or ident not in custody.REQUIRED
                    or ident in slots or item.get("status") != "BLOCKED_UNVERIFIED"
                    or not isinstance(missing, list) or not missing
                    or any(not isinstance(code, str) or not code
                           or not custody.SAFE_SEGMENT.fullmatch(code)
                           for code in missing)
                    or len(missing) != len(set(missing))):
                _deny("SOURCE_INVENTORY_UNVERIFIABLE")
            slots[ident] = set(missing)
        if set(slots) != custody.REQUIRED:
            _deny("SOURCE_INVENTORY_UNVERIFIABLE")
        return slots, raw
    except BatchError:
        raise
    except (OSError, ValueError, KeyError, TypeError, OverflowError):
        _deny("SOURCE_INVENTORY_UNVERIFIABLE")


def _write_new(path: Path, data: dict) -> tuple[int, int]:
    fd = None
    created_identity = None
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     0o600)
        identity = os.fstat(fd)
        created_identity = (identity.st_dev, identity.st_ino)
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            fd = None
            stream.write(json.dumps(data, sort_keys=True, indent=2,
                                    ensure_ascii=True) + "\n")
            stream.flush()
            os.fsync(stream.fileno())
        return created_identity
    except OSError:
        # A failed write/fsync may have left a partial, private file. Remove
        # only the inode that THIS invocation exclusively created.
        _unlink_ours(path, created_identity)
        _deny("PRIVATE_OUTPUT_WRITE_FAILED")
    finally:
        if fd is not None:
            os.close(fd)


def _unlink_ours(path: Path, identity: tuple[int, int] | None) -> None:
    if identity is None:
        return
    try:
        current = os.stat(path, follow_symlinks=False)
        if (current.st_dev, current.st_ino) == identity:
            path.unlink()
    except OSError:
        pass


def build(root: Path, evidence_root: Path, request_file: Path,
          output_manifest: Path, output_report: Path) -> dict:
    root = root.resolve()
    request = _read_request(request_file, root)
    manifest_path = _private_path(output_manifest, root, output=True)
    report_path = _private_path(output_report, root, output=True)
    if manifest_path == report_path or manifest_path == request_file.absolute() \
            or report_path == request_file.absolute():
        _deny("PRIVATE_PATHS_MUST_BE_DISTINCT")
    slots, inventory_raw = _source_inventory(root)
    if (set(request) != {"schema_version", "scope", "entries"}
            or type(request["schema_version"]) is not int
            or request["schema_version"] != 1
            or request["scope"] != REQUEST_SCOPE
            or not isinstance(request["entries"], list)
            or not 0 < len(request["entries"]) <= MAX_ENTRIES):
        _deny("INVALID_PRIVATE_REQUEST_SCHEMA")
    if custody._inside(evidence_root.absolute(), root):
        _deny("PRIVATE_EVIDENCE_ROOT_REQUIRED")

    entries = []
    seen = set()
    covered = set()
    for row in request["entries"]:
        if not isinstance(row, dict) or set(row) != REQUEST_ENTRY_FIELDS:
            _deny("INVALID_PRIVATE_REQUEST_ENTRY")
        dim, blocker = row["dimension"], row["blocker"]
        file = row["artifact"]
        custodian = row["custodian_ref"]
        if not isinstance(dim, str) or dim not in slots:
            _deny("UNRECOGNIZED_RELEASE_DIMENSION")
        if not isinstance(blocker, str) or blocker not in slots[dim]:
            _deny("UNRECOGNIZED_RELEASE_BLOCKER")
        if not custody._valid_artifact_name(file):
            _deny("UNSAFE_EVIDENCE_ARTIFACT_NAME")
        if (not isinstance(custodian, str)
                or not custody.CUSTODIAN_REF.fullmatch(custodian)):
            _deny("INVALID_OPAQUE_CUSTODIAN_REF")
        identity = dim, blocker, file
        if identity in seen:
            _deny("DUPLICATE_PRIVATE_REQUEST_ENTRY")
        seen.add(identity)
        try:
            digest = custody._hash_private_file(evidence_root.absolute(),
                                                 file, root)
        except (ValueError, OSError):
            _deny("PRIVATE_EVIDENCE_BYTE_INTEGRITY_UNVERIFIABLE")
        entries.append({"dimension": dim, "blocker": blocker,
                        "artifact": file, "sha256": digest,
                        "custodian_ref": custodian, "review_state": "UNREVIEWED"})
        covered.add((dim, blocker))

    manifest = {
        "schema_version": 1,
        "scope": custody.SCOPE,
        "source_inventory_sha256": hashlib.sha256(inventory_raw).hexdigest(),
        "entries": sorted(entries, key=lambda r: (
            r["dimension"], r["blocker"], r["artifact"])),
    }
    manifest_identity = None
    report_identity = None
    try:
        manifest_identity = _write_new(manifest_path, manifest)
        recheck = custody.inspect(root, evidence_root.absolute(), manifest_path)
        if (recheck["status"] != "INTACT_FILES_UNREVIEWED"
                or recheck["submitted_files_integrity_checked"] != len(entries)):
            _deny("POST_WRITE_INTEGRITY_RECHECK_FAILED")
        dimensions = []
        for dim in sorted(slots):
            present = sorted(blocker for blocker in slots[dim]
                             if (dim, blocker) in covered)
            missing = sorted(slots[dim] - set(present))
            dimensions.append({
                "dimension": dim, "status": "BLOCKED_UNVERIFIED",
                "unreviewed_file_slots": present,
                "still_without_file_slots": missing,
            })
        report = {
            "schema_version": 1, "scope": REPORT_SCOPE,
            "status": "UNREVIEWED_FILES_INTACT_NOT_APPROVED",
            "dimensions": dimensions,
            "dimension_count": 17,
            "submitted_files_integrity_checked": len(entries),
            "blocker_slots_with_unreviewed_files": len(covered),
            "unreviewed_files_are_authority": False,
            "external_evidence_verified": False,
            "independent_review_observed": False,
            "member_authentication_verified": False,
            "national_release_allowed": False,
            "release_allowed": False,
            "official_release_admission": "DENIED_BY_DEFAULT",
            "overall_product_completion_percentage": None,
        }
        report_identity = _write_new(report_path, report)
        return report
    except (BatchError, OSError):
        _unlink_ours(report_path, report_identity)
        _unlink_ours(manifest_path, manifest_identity)
        raise


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[2])
    parser.add_argument("--evidence-root", type=Path, required=True)
    parser.add_argument("--request-file", type=Path, required=True)
    parser.add_argument("--output-manifest", type=Path, required=True)
    parser.add_argument("--output-report", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        report = build(args.root, args.evidence_root, args.request_file,
                       args.output_manifest, args.output_report)
    except BatchError as error:
        print("DENIED: " + str(error), file=sys.stderr)
        return 1
    except Exception:
        print("DENIED: PRIVATE_EVIDENCE_BATCH_UNVERIFIABLE", file=sys.stderr)
        return 1
    print("Private evidence batch: " + report["status"] +
          "; file_count=" + str(report["submitted_files_integrity_checked"]) +
          "; all 17 dimensions BLOCKED_UNVERIFIED; release_allowed=false.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
