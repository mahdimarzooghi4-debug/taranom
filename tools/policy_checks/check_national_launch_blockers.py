#!/usr/bin/env python3
"""TRN-TECH-045: release DENY-ONLY source-blocker inventory contract.

A PASS means the blocker registry is structurally complete, NOT that national
launch, provider onboarding, legal permissions or Production are ready.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
from typing import Any

MANIFEST = Path("docs/governance/national-launch-blockers.json")
SCOPE = "FIRST_COMPREHENSIVE_NATIONAL_OFFICIAL_LAUNCH"
REQUIRED = tuple([f"TRN-EP-{n:02d}" for n in range(1, 14)] + [
    "MARKET-PUBLIC", "MARKET-SUPPORT", "MARKET-CORPORATE", "NATIONAL-COVERAGE",
])
POLICIES = ("TRN-BIZ-028", "TRN-BIZ-030", "TRN-BIZ-040")
# Every required domain has at least one irreducible external evidence blocker.
MINIMUM_BLOCKER = {
    "TRN-EP-01": "VERIFIED_MEMBER_IDENTITY",
    "TRN-EP-02": "DATA_RIGHTS_AND_CONSENT",
    "TRN-EP-03": "AUTHENTIC_MEMBERSHIP_AND_ACTIVATION",
    "TRN-EP-04": "PROVIDER_LICENSE_AND_CONTRACT",
    "TRN-EP-05": "CLINICAL_SAFETY_AND_RESPONSIBILITY",
    "TRN-EP-06": "DATA_RIGHTS_AND_CONSENT",
    "TRN-EP-07": "INSURER_AGREEMENT_AND_ENTITLEMENT",
    "TRN-EP-08": "FINANCIAL_LICENSE_BACKING_AND_AUDIT",
    "TRN-EP-09": "OFFICIAL_BILLING_AND_MEMBER_ACKNOWLEDGMENT",
    "TRN-EP-10": "COMPLAINTS_LEGAL_AUTHORITY_AND_PROCESS",
    "TRN-EP-11": "CONSENTED_AI_DATASET_AND_INDEPENDENT_EVALUATION",
    "TRN-EP-12": "PRODUCTION_HOSTING_AND_SECURITY",
    "TRN-EP-13": "VERIFIED_MEMBER_IDENTITY",
    "MARKET-PUBLIC": "ALL_3_MARKET_OPERATIONS",
    "MARKET-SUPPORT": "ALL_3_MARKET_OPERATIONS",
    "MARKET-CORPORATE": "ALL_3_MARKET_OPERATIONS",
    "NATIONAL-COVERAGE": "NATIONAL_COVERAGE_EVIDENCE",
}
ALLOWED_BLOCKERS = frozenset({
    "VERIFIED_MEMBER_IDENTITY", "LEGAL_OPERATOR_IDENTITY",
    "DATA_RIGHTS_AND_CONSENT", "AUTHENTIC_MEMBERSHIP_AND_ACTIVATION",
    "PROVIDER_LICENSE_AND_CONTRACT", "CLINICAL_SAFETY_AND_RESPONSIBILITY",
    "EMERGENCY_PROTOCOL_AND_CAPACITY", "INSURER_AGREEMENT_AND_ENTITLEMENT",
    "FINANCIAL_LICENSE_BACKING_AND_AUDIT",
    "OFFICIAL_BILLING_AND_MEMBER_ACKNOWLEDGMENT",
    "COMPLAINTS_LEGAL_AUTHORITY_AND_PROCESS",
    "CONSENTED_AI_DATASET_AND_INDEPENDENT_EVALUATION",
    "HUMAN_AI_COMMITTEE_AND_RELEASE_APPROVAL",
    "PRODUCTION_HOSTING_AND_SECURITY", "REAL_EXTERNAL_INTEGRATIONS",
    "INDEPENDENT_REVIEWS_AND_RELEASE_APPROVAL",
    "NATIONAL_COVERAGE_EVIDENCE", "ALL_3_MARKET_OPERATIONS",
})
ROOT_KEYS = frozenset({
    "schema_version", "scope", "source_policy_refs",
    "release_admission", "real_release_approval_observed",
    "external_evidence_verified", "statement", "items",
})
ITEM_KEYS = frozenset({
    "id", "label", "status", "policy_refs", "missing_evidence",
})
REF_PATTERN = re.compile(r"TRN-BIZ-\d{3}\Z")


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("duplicate JSON key")
        value[key] = item
    return value


def load_inventory(path: Path) -> dict[str, Any]:
    # No external URLs, dynamically imported code or auto-discovery of claims.
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream, object_pairs_hook=_unique_object,
                          parse_constant=lambda _: (_ for _ in ()).throw(
                              ValueError("non-finite JSON numeric value")))
    if not isinstance(value, dict):
        raise ValueError("inventory root must be an object")
    return value


def _refs_valid(root: Path, refs: Any) -> bool:
    if (not isinstance(refs, list) or not refs or
        not all(isinstance(x, str) for x in refs) or
        len(refs) != len(set(refs))):
        return False
    docs = root / "docs" / "decisions"
    if docs.is_symlink() or not docs.is_dir():
        return False
    for ref in refs:
        if not REF_PATTERN.fullmatch(ref):
            return False
        matches = list(docs.glob(f"{ref}-*.md"))
        if len(matches) != 1 or matches[0].is_symlink() or not matches[0].is_file():
            return False
    return True


def audit_inventory(root: Path, inventory: dict[str, Any]) -> list[str]:
    """Return deterministic denial reasons; never return a release permit."""
    issues: list[str] = []
    if set(inventory) != ROOT_KEYS:
        issues.append("incorrect root schema")
    if type(inventory.get("schema_version")) is not int or inventory["schema_version"] != 1:
        issues.append("unsupported inventory schema")
    if inventory.get("scope") != SCOPE:
        issues.append("official national scope changed or missing")
    if inventory.get("source_policy_refs") != list(POLICIES):
        issues.append("three-market comprehensive national source policies missing")
    if inventory.get("release_admission") != "DENIED_BY_DEFAULT":
        issues.append("unapproved release admission flag")
    if inventory.get("real_release_approval_observed") is not False:
        issues.append("human release approval must not be self-asserted")
    if inventory.get("external_evidence_verified") is not False:
        issues.append("external evidence must not be self-certified")
    if not isinstance(inventory.get("statement"), str) or (
        "NOT" not in inventory["statement"] or len(inventory["statement"]) > 300):
        issues.append("missing non-authorization warning")
    if not _refs_valid(root, inventory.get("source_policy_refs")):
        issues.append("source decision policy files missing or invalid")

    items = inventory.get("items")
    if not isinstance(items, list):
        return sorted(issues + ["required evidence domains missing"])
    seen: set[str] = set()
    for index, item in enumerate(items):
        if not isinstance(item, dict):
            issues.append(f"item {index}: invalid record")
            continue
        item_id = item.get("id")
        if not isinstance(item_id, str) or item_id not in REQUIRED:
            issues.append(f"item {index}: unknown mandatory domain")
            continue
        if item_id in seen:
            issues.append(f"{item_id}: duplicate domain")
        seen.add(item_id)
        if set(item) != ITEM_KEYS:
            issues.append(f"{item_id}: incorrect item schema")
        if not isinstance(item.get("label"), str) or not item["label"].strip() or (
            len(item["label"]) > 120):
            issues.append(f"{item_id}: unlabelled domain")
        if item.get("status") != "BLOCKED_UNVERIFIED":
            issues.append(f"{item_id}: unapproved READY or inferred evidence")
        if not _refs_valid(root, item.get("policy_refs")):
            issues.append(f"{item_id}: policy provenance invalid")
        evidence = item.get("missing_evidence")
        if (not isinstance(evidence, list) or not evidence or
            not all(isinstance(x, str) for x in evidence) or
            len(evidence) != len(set(evidence)) or
            not set(evidence).issubset(ALLOWED_BLOCKERS) or
            MINIMUM_BLOCKER[item_id] not in evidence):
            issues.append(f"{item_id}: missing required negative evidence")
    missing = sorted(set(REQUIRED) - seen)
    if missing:
        issues.append("missing required domains: " + ", ".join(missing))
    if len(items) != len(REQUIRED):
        issues.append("required evidence inventory size changed")
    return sorted(set(issues))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Validate national-launch blockers without granting release")
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--report-json", type=Path, default=None,
                        help="write explicitly denied, unverified blockers")
    args = parser.parse_args(argv)
    try:
        root = args.root.resolve(strict=True)
        manifest = root / MANIFEST
        if manifest.is_symlink() or not manifest.is_file():
            raise ValueError("no trusted on-repo blocker inventory")
        inventory = load_inventory(manifest)
        problems = audit_inventory(root, inventory)
    except (OSError, ValueError, UnicodeError, TypeError) as exc:
        print(f"RELEASE DENIED: unable to validate evidence inventory ({type(exc).__name__})",
              file=sys.stderr)
        return 1
    if problems:
        for problem in problems:
            print(f"RELEASE DENIED: {problem}", file=sys.stderr)
        return 1
    # A successful lint/check status is NOT an admissible launch gate.
    print("BLOCKER INVENTORY VALID: 13 product domains, 3 markets, national scope.")
    print("OFFICIAL RELEASE: DENIED; no actual external evidence or approvals verified.")
    if args.report_json is not None:
        # This is informational CI evidence; never machine-interpret it as
        # Production Release authorization or externally verified coverage.
        report = {
            "release_allowed": False,
            "evidence_verified": False,
            "release_admission": "DENIED",
            "scope": SCOPE,
            "unverified_domains": [
                {"id": item["id"], "missing_evidence": item["missing_evidence"]}
                for item in inventory["items"]
            ],
        }
        try:
            args.report_json.parent.mkdir(parents=True, exist_ok=True)
            args.report_json.write_text(
                json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8")
        except OSError as exc:
            print(f"RELEASE DENIED: report output failed ({type(exc).__name__})",
                  file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
