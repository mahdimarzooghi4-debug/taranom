#!/usr/bin/env python3
"""TRN-TECH-158: offline, denial-only EXTERNAL custody discovery evaluator.

A human-supplied opaque evidence reference is NOT evidence of its provenance,
authenticity, owner, lawfulness, retention or approval. No external connection,
transport, custody provider, receipt algorithm or human decision is defined.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import stat
import sys

SCHEMA = 1
SCOPE = "TRN_TECH_158_WORKER_EXTERNAL_CUSTODY_DISCOVERY"
MAX_BYTES = 16384
TOPICS = (
    "distinct_administrative_control",
    "external_custodian_authority",
    "external_receipt_provenance",
    "off_host_history_anchor",
    "retention_and_deletion_authority",
    "privacy_rights_and_data_minimization",
    "transport_and_service_identity",
    "incident_recovery_and_availability",
    "independent_human_legal_review",
)
ROOT_FIELDS = {"schema_version", "scope", "candidate"}
CANDIDATE_FIELDS = {"topic_refs"}
REFERENCE = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{5,79}\Z")

class InvalidCustodyContract(ValueError):
    pass

def reject(reason: str):
    raise InvalidCustodyContract("INVALID_EXTERNAL_CUSTODY_CONTRACT_" + reason)

def unique(pairs):
    out = {}
    for k, v in pairs:
        if k in out:
            reject("DUPLICATE_KEY")
        out[k] = v
    return out

def reject_constant(_):
    reject("NONFINITE_NUMBER")

def inspect_bytes(raw: bytes) -> dict:
    """Pure syntax and completeness check, NEVER verifies claims/authorities."""
    result = {
        "schema_version": SCHEMA,
        "scope": SCOPE,
        "status": "DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",
        "candidate_topics_supplied": 0,
        "missing_topics": list(TOPICS),
        "independent_custody_verified": False,
        "external_receipt_verified": False,
        "retention_and_privacy_authorized": False,
        "human_release_approved": False,
        "production_ready": False,
        "national_release_allowed": False,
    }
    try:
        if not isinstance(raw, bytes) or len(raw) > MAX_BYTES or not raw:
            reject("INPUT_BOUND")
        try:
            data = json.loads(raw.decode("utf-8"), object_pairs_hook=unique,
                              parse_constant=reject_constant)
        except (UnicodeError, json.JSONDecodeError):
            reject("MALFORMED_JSON")
        if (type(data) is not dict or set(data) != ROOT_FIELDS or
            type(data["schema_version"]) is not int or
            data["schema_version"] != SCHEMA or data["scope"] != SCOPE):
            reject("ROOT_SCHEMA")
        candidate = data["candidate"]
        if candidate is None:
            result["status"] = "EXTERNAL_CUSTODY_NOT_CONFIGURED"
            return result
        if type(candidate) is not dict or set(candidate) != CANDIDATE_FIELDS:
            reject("CLAIM_FIELDS")
        refs = candidate["topic_refs"]
        if type(refs) is not dict or len(refs) > len(TOPICS):
            reject("REFERENCE_SET")
        if any(topic not in TOPICS or
               type(value) is not str or
               REFERENCE.fullmatch(value) is None or
               value.lower().startswith(("http:", "https:", "file:", "token:", "secret:"))
               for topic, value in refs.items()):
            reject("REFERENCE_FORMAT")
        result["candidate_topics_supplied"] = len(refs)
        result["missing_topics"] = [name for name in TOPICS if name not in refs]
        # Claim completeness only. Never move into APPROVED, READY, ACTIVE,
        # VERIFIED or independent custody based on claimant-controlled data.
        result["status"] = ("EXTERNAL_CUSTODY_CLAIMS_REQUIRE_INDEPENDENT_REVIEW"
                            if not result["missing_topics"] else
                            "EXTERNAL_CUSTODY_CLAIMS_INCOMPLETE")
        return result
    except InvalidCustodyContract:
        return result

def _secure_input(path: Path) -> bytes:
    if path.is_symlink() or any(parent.is_symlink() for parent in path.absolute().parents):
        reject("SYMLINK_PATH")
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    fd = os.open(path, flags)
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > MAX_BYTES:
            reject("FILE_TYPE_OR_SIZE")
        with os.fdopen(os.dup(fd), "rb") as stream:
            raw = stream.read(MAX_BYTES + 1)
        after = os.fstat(fd)
        if (len(raw) > MAX_BYTES or before.st_dev != after.st_dev or
            before.st_ino != after.st_ino or before.st_size != after.st_size or
            before.st_mtime_ns != after.st_mtime_ns or before.st_ctime_ns != after.st_ctime_ns):
            reject("FILE_RACE_OR_SIZE")
        return raw
    finally:
        os.close(fd)

def _write_denial_only(path: Path, result: dict):
    parent = path.parent
    if (not parent.is_dir() or path.is_symlink() or parent.is_symlink() or
        not path.is_absolute()):
        reject("REPORT_PATH")
    flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0)
    data = (json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n").encode()
    fd = os.open(path, flags, 0o600)
    with os.fdopen(fd, "wb") as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())

def main():
    parser = argparse.ArgumentParser(description="Non-authorizing Worker external-custody discovery")
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--report-json", required=True)
    args = parser.parse_args()
    try:
        # Refuse dangerous private material/actual external bytes in CI:
        # the only permitted repository-sourced manifest is the empty template.
        raw = _secure_input(Path(args.manifest))
        report = inspect_bytes(raw)
        _write_denial_only(Path(args.report_json).absolute(), report)
        print("WORKER_EXTERNAL_CUSTODY_" + report["status"])
    except (OSError, InvalidCustodyContract, ValueError):
        # Fixed code only, no paths, partner refs, HMACs or evidence contents.
        print("WORKER_EXTERNAL_CUSTODY_DENIED", file=sys.stderr)
        raise SystemExit(17) from None

if __name__ == "__main__":
    main()
