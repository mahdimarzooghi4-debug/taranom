#!/usr/bin/env python3
"""TRN-TECH-082: compare two independently byte-verified private evidence rounds.

Output proves only a bounded difference in UNREVIEWED submitted file bytes;
never real-provider legitimacy, legal authority, approval or national readiness.
No network and NO actual evidence in public GitHub/CI.
"""
from __future__ import annotations

import argparse
from collections import Counter
import os
from pathlib import Path
import stat
import sys

import build_private_evidence_batch as batch
import check_private_external_evidence as custody

SCOPE = "PRIVATE_EXTERNAL_EVIDENCE_CHANGE_RECONCILIATION"
STATUS = "UNREVIEWED_EVIDENCE_DELTA_NOT_APPROVAL"


def _fail(code: str) -> None:
    raise batch.BatchError(code)


def _private_root(path: Path, public: Path) -> Path:
    path = batch._private_path(path, public, output=False)
    try:
        st = path.stat()
        if (not stat.S_ISDIR(st.st_mode) or st.st_uid != os.getuid()
                or stat.S_IMODE(st.st_mode) & 0o077):
            _fail("PRIVATE_EVIDENCE_ROOT_NOT_OWNER_ONLY")
    except OSError:
        _fail("PRIVATE_EVIDENCE_ROOT_UNAVAILABLE")
    return path


def _checked_manifest(public: Path, evidence: Path, manifest: Path) -> dict:
    # Separate owner-only path read is stricter than the generic custody
    # checker: it rejects hardlinked or group/world-readable manifests.
    first = batch._read_request(manifest, public)
    if not first.get("entries") or not isinstance(first["entries"], list):
        _fail("EMPTY_OR_INVALID_PRIVATE_SNAPSHOT")
    check = custody.inspect(public, evidence, manifest)
    if (check["status"] != "INTACT_FILES_UNREVIEWED"
            or check["errors"]
            or check["submitted_files_integrity_checked"] != len(first["entries"])):
        _fail("PRIVATE_SNAPSHOT_BYTE_INTEGRITY_UNVERIFIABLE")
    second = batch._read_request(manifest, public)
    if first != second:
        _fail("PRIVATE_SNAPSHOT_CHANGED_DURING_VALIDATION")
    # Exact manifest field and record type are established by inspect().
    return first


def _group(entries: list) -> dict[tuple[str, str], Counter]:
    mapped: dict[tuple[str, str], Counter] = {}
    for row in entries:
        slot = row["dimension"], row["blocker"]
        mapped.setdefault(slot, Counter())[row["sha256"]] += 1
    return mapped


def compare(public: Path, before_evidence: Path, before_manifest: Path,
            after_evidence: Path, after_manifest: Path,
            output_report: Path) -> dict:
    public = public.resolve()
    before_root = _private_root(before_evidence, public)
    after_root = _private_root(after_evidence, public)
    before_path = batch._private_path(before_manifest, public, output=False)
    after_path = batch._private_path(after_manifest, public, output=False)
    dest = batch._private_path(output_report, public, output=True)

    try:
        before_stat, after_stat = before_root.stat(), after_root.stat()
        if (before_stat.st_dev, before_stat.st_ino) == (
                after_stat.st_dev, after_stat.st_ino):
            _fail("INDEPENDENT_SNAPSHOT_ROOTS_REQUIRED")
    except OSError:
        _fail("PRIVATE_EVIDENCE_ROOT_UNAVAILABLE")

    if (before_path == after_path or dest in (before_path, after_path)
            or dest.parent in (before_path.parent, after_path.parent)
            or dest.is_relative_to(before_root)
            or dest.is_relative_to(after_root)):
        _fail("PRIVATE_INPUT_OUTPUT_SEPARATION_REQUIRED")

    slots, source = batch._source_inventory(public)
    previous = _checked_manifest(public, before_root, before_path)
    current = _checked_manifest(public, after_root, after_path)
    older = _group(previous["entries"])
    newer = _group(current["entries"])

    dimensions = []
    changed = 0
    newly_present = 0
    withdrawn = 0
    retained = 0
    for dimension in sorted(slots):
        entries = []
        changed_here = 0
        for blocker in sorted(slots[dimension]):
            prior = older.get((dimension, blocker), Counter())
            latest = newer.get((dimension, blocker), Counter())
            identical = sum((prior & latest).values())
            added = sum(latest.values()) - identical
            removed = sum(prior.values()) - identical
            if added and removed:
                state = "REPLACED_OR_CHANGED_UNREVIEWED"
            elif added:
                state = "ADDED_UNREVIEWED"
            elif removed:
                state = "REMOVED_UNREVIEWED"
            elif identical:
                state = "RETAINED_UNREVIEWED"
            else:
                state = "NO_FILES_IN_EITHER_ROUND"
            if added or removed:
                changed_here += 1
                changed += 1
            newly_present += added
            withdrawn += removed
            retained += identical
            entries.append({
                "blocker": blocker,
                "change": state,
                "prior_unreviewed_files": sum(prior.values()),
                "current_unreviewed_files": sum(latest.values()),
                "byte_identical_retained": identical,
                "new_or_changed_file_records": added,
                "removed_or_replaced_file_records": removed,
            })
        dimensions.append({
            "dimension": dimension, "status": "BLOCKED_UNVERIFIED",
            "changed_blocker_slots": changed_here, "blocker_slots": entries,
        })

    # Ensure source categories/policy are unchanged since the input checks,
    # then re-check both actual byte sets immediately before private output.
    if batch._source_inventory(public)[1] != source:
        _fail("SOURCE_INVENTORY_CHANGED_DURING_RECONCILIATION")
    for evidence, manifest, expected in (
        (before_root, before_path, previous),
        (after_root, after_path, current),
    ):
        check = custody.inspect(public, evidence, manifest)
        if (check["status"] != "INTACT_FILES_UNREVIEWED"
                or check["submitted_files_integrity_checked"] != len(expected["entries"])
                or batch._read_request(manifest, public) != expected):
            _fail("PRIVATE_SNAPSHOT_CHANGED_DURING_RECONCILIATION")
    report = {
        "schema_version": 1, "scope": SCOPE, "status": STATUS,
        "dimensions": dimensions,
        "dimension_count": 17,
        "previous_integrity_checked_file_records": len(previous["entries"]),
        "current_integrity_checked_file_records": len(current["entries"]),
        "changed_blocker_slots": changed,
        "byte_identical_retained_file_records": retained,
        "new_or_changed_file_records": newly_present,
        "removed_or_replaced_file_records": withdrawn,
        "external_evidence_verified": False,
        "independent_review_observed": False,
        "medical_or_financial_authority_verified": False,
        "member_authentication_verified": False,
        "release_allowed": False,
        "national_release_allowed": False,
        "official_release_admission": "DENIED_BY_DEFAULT",
        "overall_product_completion_percentage": None,
    }
    identity = None
    try:
        identity = batch._write_new(dest, report)
        if batch._read_request(dest, public) != report:
            _fail("PRIVATE_REPORT_INTEGRITY_UNVERIFIABLE")
        return report
    except (batch.BatchError, OSError):
        batch._unlink_ours(dest, identity)
        raise


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[2])
    parser.add_argument("--before-evidence-root", type=Path, required=True)
    parser.add_argument("--before-manifest", type=Path, required=True)
    parser.add_argument("--after-evidence-root", type=Path, required=True)
    parser.add_argument("--after-manifest", type=Path, required=True)
    parser.add_argument("--output-report", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        result = compare(args.root, args.before_evidence_root,
                         args.before_manifest, args.after_evidence_root,
                         args.after_manifest, args.output_report)
    except batch.BatchError as exc:
        print("DENIED: " + str(exc), file=sys.stderr)
        return 1
    except Exception:
        print("DENIED: PRIVATE_EVIDENCE_RECONCILIATION_UNVERIFIABLE",
              file=sys.stderr)
        return 1
    print("Private evidence delta: " + result["status"] +
          "; dimensions=17; changed_slots=" +
          str(result["changed_blocker_slots"]) +
          "; external_evidence_verified=false; release_allowed=false.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
