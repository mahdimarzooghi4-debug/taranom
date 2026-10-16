#!/usr/bin/env python3
"""TRN-TECH-138: Stage 4 TEST preparation, DENY-ONLY and OFFLINE.

The subject "Stage 4" is NOT an accepted environment or formal release gate.
This program can audit the repository snapshot; it cannot inspect live GitHub
approvals, private Stage infrastructure, actual secrets, real providers or
independent review. Exit 0 means a faithfully BLOCKED report was produced.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from check_national_launch_blockers import (
    MANIFEST as NATIONAL_MANIFEST, REQUIRED, audit_inventory, load_inventory,
)

CONTRACT = "TRN-TECH-138"
MANIFEST = Path("docs/governance/stage4-test-preflight.json")
REQUIRED_PR = tuple(range(132, 141))
GATES = (
    "FORMAL_STAGE4_SCOPE_AND_ENVIRONMENT",
    "EPHEMERAL_NETWORK_AND_DATA_ISOLATION",
    "INDEPENDENT_CODE_AND_SECURITY_APPROVAL",
    "EXPLICIT_STAGE_TEST_DEPLOYMENT_AUTHORIZATION",
    "ISOLATED_STAGE_BACKUP_AND_ROLLBACK_EVIDENCE",
    "STAGE_SERVICE_IDENTITY_AND_PERMISSION_MODEL",
    "GITHUB_STACK_INTEGRATION_AND_EXACT_HEAD_QA",
)
ROOT = frozenset({
    "schema_version", "contract", "intent", "scope", "environment",
    "stage_test_admission", "stage_deployment_authorized",
    "production_release_allowed", "real_user_access_allowed",
    "external_partner_access_verified", "reviewers_independently_approved",
    "github_snapshot", "pending_stage_gates", "national_blockers_manifest",
    "non_authorization_note",
})
REQUIRED_SNAPSHOT = frozenset({"observed_date", "is_live_verification",
                                "pr_numbers", "chain_is_merged"})
DATE = re.compile(r"20[0-9]{2}-[01][0-9]-[0-3][0-9]\Z")

def _object_no_duplicates(pairs):
    value = {}
    for k, v in pairs:
        if k in value:
            raise ValueError("DUPLICATE_JSON_KEY")
        value[k] = v
    return value

def inspect(root: Path) -> dict:
    findings = set()
    inventory = {}
    try:
        path = root / MANIFEST
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 32768:
            raise ValueError("PRESTAGE_MANIFEST_UNTRUSTED")
        inventory = json.loads(path.read_text("utf-8"),
                               object_pairs_hook=_object_no_duplicates)
        if type(inventory) is not dict or set(inventory) != ROOT:
            findings.add("MANIFEST_SCHEMA_UNVERIFIABLE")
        else:
            if type(inventory.get("schema_version")) is not int or inventory["schema_version"] != 1:
                findings.add("UNSUPPORTED_STAGE_SCHEMA")
            if inventory["contract"] != CONTRACT or inventory["intent"] != "STAGE4_TEST_PREPARATION_ONLY":
                findings.add("STAGE_INTENT_UNVERIFIABLE")
            if inventory["scope"] != "EPHEMERAL_SYNTHETIC_REHEARSAL_ONLY":
                findings.add("STAGE_SCOPE_ESCALATION")
            if inventory["environment"] != "UNDEFINED_NO_STAGE4_TARGET_CONFIRMED":
                findings.add("STAGE_ENVIRONMENT_FABRICATED")
            expected = {"stage_test_admission": "DENIED_UNVERIFIED",
                        "stage_deployment_authorized": False,
                        "production_release_allowed": False,
                        "real_user_access_allowed": False,
                        "external_partner_access_verified": False,
                        "reviewers_independently_approved": False}
            for key, value in expected.items():
                if type(inventory.get(key)) is not type(value) or inventory[key] != value:
                    findings.add("UNAUTHORIZED_STAGE_OR_PRODUCTION_ADMISSION")
            if inventory["national_blockers_manifest"] != str(NATIONAL_MANIFEST):
                findings.add("NATIONAL_BLOCKER_SOURCE_REPLACED")
            if inventory["pending_stage_gates"] != list(GATES):
                findings.add("STAGE_GATE_COVERAGE_INCOMPLETE")
            snapshot = inventory["github_snapshot"]
            if type(snapshot) is not dict or set(snapshot) != REQUIRED_SNAPSHOT:
                findings.add("GITHUB_SNAPSHOT_SCHEMA")
            elif (
                type(snapshot["observed_date"]) is not str or
                not DATE.fullmatch(snapshot["observed_date"]) or
                snapshot["is_live_verification"] is not False or
                snapshot["chain_is_merged"] is not False or
                type(snapshot["pr_numbers"]) is not list or
                snapshot["pr_numbers"] != list(REQUIRED_PR)
            ):
                findings.add("UNTRUSTED_OR_INCOMPLETE_PR_CHAIN")
            if (type(inventory["non_authorization_note"]) is not str or
                not 60 <= len(inventory["non_authorization_note"]) <= 500 or
                "NOT" not in inventory["non_authorization_note"]):
                findings.add("MISSING_NO_AUTHORIZATION_WARNING")
    except (OSError, TypeError, UnicodeError, ValueError, RecursionError):
        findings.add("STAGE_MANIFEST_UNAVAILABLE_OR_INVALID")

    try:
        national_path = root / NATIONAL_MANIFEST
        if national_path.is_symlink() or not national_path.is_file():
            raise ValueError("UNTRUSTED_NATIONAL_BLOCKER")
        national = load_inventory(national_path)
        issues = audit_inventory(root, national)
        if issues or len(national["items"]) != len(REQUIRED):
            findings.add("NATIONAL_SOURCE_INVENTORY_INVALID")
    except (OSError, ValueError, KeyError, TypeError, UnicodeError, RecursionError):
        findings.add("NATIONAL_SOURCE_INVENTORY_INVALID")

    # "consistent" is ONLY about bounded source shape, not Stage admission.
    return {
        "contract": CONTRACT,
        "stage4_test_admission": "DENIED",
        "stage_deployment_allowed": False,
        "production_release_allowed": False,
        "stage4_target_verified": False,
        "independent_review_verified": False,
        "external_evidence_verified": False,
        "live_pr_checks_performed": False,
        "synthetic_ci_is_not_stage": True,
        "source_snapshot_consistent": not findings,
        "scope": "EPHEMERAL_SYNTHETIC_REHEARSAL_ONLY",
        "candidate_prs_from_snapshot": list(REQUIRED_PR),
        "unverified_stage_gates": list(GATES),
        "national_unverified_dimensions": len(REQUIRED) if not findings else None,
        "findings": sorted(findings),
    }

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--report-json", type=Path, required=True)
    a = parser.parse_args(argv)
    report = inspect(a.root)
    try:
        path = a.report_json
        if path.is_symlink():
            raise ValueError("REPORT_SYMLINK_FORBIDDEN")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report, sort_keys=True, indent=2) + "\n",
                        encoding="utf-8")
    except (OSError, ValueError, UnicodeError):
        print("DENIED STAGE4_REPORT_UNWRITABLE", file=sys.stderr)
        return 1
    print("STAGE 4 TEST ADMISSION: DENIED (informational source check only).")
    for reason in report["findings"]:
        print("DENIED " + reason, file=sys.stderr)
    return 0 if report["source_snapshot_consistent"] else 1

if __name__ == "__main__":
    raise SystemExit(main())
