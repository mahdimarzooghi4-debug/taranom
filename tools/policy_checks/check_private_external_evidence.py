#!/usr/bin/env python3
"""TRN-TECH-064: offline custody check for PRIVATE external launch evidence.

No evidence or secrets should be committed to this public repository. Digest
integrity only; even success NEVER grants legal, clinical or release approval.
"""
from __future__ import annotations

import argparse
import hashlib
import json
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


def _read_json(path: Path) -> tuple[dict, bytes]:
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_MANIFEST:
        raise ValueError("INVALID_MANIFEST_FILE")
    raw = path.read_bytes()
    try:
        data = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique_pairs,
                          parse_constant=_reject_constant)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError("INVALID_JSON") from error
    if not isinstance(data, dict):
        raise ValueError("INVALID_JSON_ROOT")
    return data, raw


def _inside(path: Path, root: Path) -> bool:
    return path.resolve().is_relative_to(root.resolve())


def _valid_artifact_name(name) -> bool:
    return (isinstance(name, str) and 0 < len(name) <= 200
            and not name.startswith("/") and "\\" not in name
            and all(SAFE_SEGMENT.fullmatch(p) and p not in {".", ".."}
                    for p in name.split("/"))
            and len(PurePosixPath(name).parts) == len(name.split("/")))


def _hash_private_file(evidence_root: Path, name: str, public_root: Path) -> str:
    if not _valid_artifact_name(name):
        raise ValueError("UNSAFE_ARTIFACT_REFERENCE")
    if evidence_root.is_symlink() or not evidence_root.is_dir():
        raise ValueError("PRIVATE_EVIDENCE_ROOT_UNAVAILABLE")
    current = evidence_root
    for segment in name.split("/"):
        current = current / segment
        if current.is_symlink():
            raise ValueError("SYMLINKED_ARTIFACT_FORBIDDEN")
    if not current.is_file() or not _inside(current, evidence_root):
        raise ValueError("ARTIFACT_UNAVAILABLE")
    if _inside(current, public_root):
        raise ValueError("PUBLIC_REPOSITORY_EVIDENCE_FORBIDDEN")
    size = current.stat().st_size
    if not 0 < size <= MAX_ARTIFACT:
        raise ValueError("ARTIFACT_SIZE_UNVERIFIABLE")
    h = hashlib.sha256()
    with current.open("rb") as stream:
        while data := stream.read(65536):
            h.update(data)
    return h.hexdigest()


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

        data, _ = _read_json(manifest)
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
            "ARTIFACT_SHA256_MISMATCH",
        }
        errors.append(known if known in allowed else "INTAKE_UNVERIFIABLE")
    report["errors"] = sorted(set(errors))
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--evidence-root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.root.resolve(), args.evidence_root, args.manifest)
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print("External-evidence custody: " + report["status"] +
          "; checked_files=" + str(report["submitted_files_integrity_checked"]) +
          "; release_allowed=false; independent_review=false.")
    if report["errors"]:
        print("DENIED: " + ", ".join(report["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
