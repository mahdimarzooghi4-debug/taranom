#!/usr/bin/env python3
"""TRN-TECH-069: conservative, reproducible candidate Epic evidence accounting.

DO NOT infer product completion percentages, contract approval or real-world
acceptance from a file existing, passing synthetic tests or a green PR CI.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REGISTRY = Path("docs/governance/epic-capability-progress-draft.json")
BLOCKERS = Path("docs/governance/national-launch-blockers.json")
ROOT_KEYS = frozenset({"schema_version", "scope", "baseline_status", "weights_status", "method", "capabilities"})
CAP_KEYS = frozenset({"epic_id", "capability_id", "capability", "decision_refs",
                      "implementation_ref", "test_ref", "evidence_class",
                      "formal_acceptance_observed"})
ALL_EPICS = [f"TRN-EP-{n:02d}" for n in range(1, 14)]
MARKETS = {"MARKET-PUBLIC", "MARKET-SUPPORT", "MARKET-CORPORATE", "NATIONAL-COVERAGE"}
ALLOWED_SCOPES = {"SYNTHETIC_INTERNAL", "DISCONNECTED_UI_ONLY",
                  "OFFLINE_POLICY_ONLY", "NO_EVIDENCE"}
CAP = re.compile(r"TRN-CAP-(\d{2})-(0[1-3])\Z")
DEC = re.compile(r"TRN-BIZ-\d{3}\Z")
MAX_BYTES = 128 * 1024


def _unique(pairs):
    result = {}
    for k, v in pairs:
        if k in result:
            raise ValueError("DUPLICATE_JSON_KEY")
        result[k] = v
    return result


def _read(path):
    if path.is_symlink() or not path.is_file() or path.stat().st_size > MAX_BYTES:
        raise ValueError("MISSING_OR_UNSAFE_SOURCE")
    raw = path.read_bytes()
    if len(raw) > MAX_BYTES:
        raise ValueError("MISSING_OR_UNSAFE_SOURCE")
    try:
        return json.loads(raw.decode("utf-8"), object_pairs_hook=_unique,
                          parse_constant=lambda _: (_ for _ in ()).throw(ValueError("NONFINITE_JSON")))
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError("INVALID_JSON") from error


def _inside(path, root):
    return path.resolve().is_relative_to(root.resolve())


def _safe_code_ref(root, name, prefix):
    if (not isinstance(name, str) or not name.startswith(prefix)
            or not name.endswith((".cs", ".py", ".tsx", ".ts"))
            or any(part in (".", "..") for part in Path(name).parts)
            or "\\" in name):
        raise ValueError("UNSAFE_OR_UNMAPPED_EVIDENCE_REFERENCE")
    path = root / name
    if not _inside(path, root) or path.is_symlink() or not path.is_file():
        raise ValueError("MISSING_OR_UNSAFE_EVIDENCE_REFERENCE")
    # Prevent a symlinked parent within the repo from escaping the scope.
    if any(p.is_symlink() for p in list(path.parents)[:-1]):
        raise ValueError("MISSING_OR_UNSAFE_EVIDENCE_REFERENCE")


def inspect(root: Path) -> dict:
    result = {
        "schema_version": 1,
        "scope": "COMPREHENSIVE_NATIONAL_PRODUCT_EPIC_MEASUREMENT",
        "status": "DENIED_INVALID_MEASUREMENT",
        "baseline_approved": False,
        "overall_implementation_completion_pct": None,
        "formal_acceptance_pct": None,
        "production_ready": False,
        "national_release_allowed": False,
        "epic_count": 0,
        "provisional_capabilities": 0,
        "references_to_test_only_or_disconnected_code": 0,
        "capabilities_without_code_test_links": 0,
        "formally_accepted_capabilities_recorded": 0,
        "external_blockers_unverified": 0,
        "epics": [],
        "errors": [],
    }
    try:
        r = _read(root / REGISTRY)
        b = _read(root / BLOCKERS)
        if (not isinstance(r, dict) or set(r) != ROOT_KEYS
                or type(r["schema_version"]) is not int or r["schema_version"] != 1
                or r["scope"] != result["scope"]
                or r["baseline_status"] != "DRAFT_UNAPPROVED"
                or r["weights_status"] != "NOT_APPROVED"
                or r["method"] != "THREE_CANDIDATE_ACCEPTANCE_CHECKS_PER_EPIC_NOT_FINAL_SCOPE"
                or not isinstance(r["capabilities"], list)):
            raise ValueError("MEASUREMENT_BASELINE_INVALID")
        if (not isinstance(b, dict)
                or b.get("release_admission") != "DENIED_BY_DEFAULT"
                or b.get("external_evidence_verified") is not False
                or b.get("real_release_approval_observed") is not False
                or not isinstance(b.get("items"), list)
                or len(b["items"]) != 17):
            raise ValueError("RELEASE_BLOCKERS_UNVERIFIED_REQUIRED")
        byid = {}
        for item in b["items"]:
            if (not isinstance(item, dict) or not isinstance(item.get("id"), str)
                    or item["id"] in byid or item.get("status") != "BLOCKED_UNVERIFIED"
                    or not isinstance(item.get("policy_refs"), list)
                    or not item["policy_refs"]):
                raise ValueError("RELEASE_BLOCKERS_UNVERIFIED_REQUIRED")
            byid[item["id"]] = item
        if set(byid) != set(ALL_EPICS) | MARKETS:
            raise ValueError("ALL_NATIONAL_MARKETS_AND_EPICS_REQUIRED")
        if len(r["capabilities"]) != 39:
            raise ValueError("CANDIDATE_CAPABILITY_SCOPE_CHANGED")
        seen = set()
        mapped = {epic: [] for epic in ALL_EPICS}
        for cap in r["capabilities"]:
            if not isinstance(cap, dict) or set(cap) != CAP_KEYS:
                raise ValueError("INVALID_CAPABILITY_SCHEMA")
            epic, cid = cap["epic_id"], cap["capability_id"]
            match = CAP.fullmatch(cid) if isinstance(cid, str) else None
            if (not isinstance(epic, str) or epic not in mapped
                    or not match or f"TRN-EP-{match.group(1)}" != epic
                    or cid in seen):
                raise ValueError("CAPABILITY_EPIC_ID_INVALID_OR_DUPLICATE")
            seen.add(cid)
            if (not isinstance(cap["capability"], str) or not cap["capability"].strip()
                    or len(cap["capability"]) > 180 or cap["formal_acceptance_observed"] is not False):
                raise ValueError("UNVERIFIED_CAPABILITY_CANNOT_SELF_APPROVE")
            if (not isinstance(cap["decision_refs"], list)
                    or cap["decision_refs"] != byid[epic]["policy_refs"]
                    or not all(isinstance(x, str) and DEC.fullmatch(x)
                               for x in cap["decision_refs"])):
                raise ValueError("CAPABILITY_PROVENANCE_MISMATCH")
            if cap["evidence_class"] not in ALLOWED_SCOPES:
                raise ValueError("CAPABILITY_EVIDENCE_CLASS_UNSUPPORTED")
            code, test = cap["implementation_ref"], cap["test_ref"]
            if cap["evidence_class"] == "NO_EVIDENCE":
                if code is not None or test is not None:
                    raise ValueError("CANNOT_COUNT_UNVERIFIED_CODE_EVIDENCE")
                result["capabilities_without_code_test_links"] += 1
            else:
                if not isinstance(code, str) or not isinstance(test, str):
                    raise ValueError("MISSING_CODE_TEST_REFERENCE_PAIR")
                if not code.startswith(("src/", "apps/", "tools/")):
                    raise ValueError("UNSAFE_OR_UNMAPPED_EVIDENCE_REFERENCE")
                if not test.startswith(("tests/", "apps/member-web/tests/", "apps/center-web/tests/")):
                    raise ValueError("UNSAFE_OR_UNMAPPED_EVIDENCE_REFERENCE")
                _safe_code_ref(root, code, code.split("/")[0] + "/")
                _safe_code_ref(root, test, test.split("/")[0] + "/")
                if cap["evidence_class"] == "DISCONNECTED_UI_ONLY" and not code.startswith("apps/"):
                    raise ValueError("EVIDENCE_CLASS_REFERENCE_MISMATCH")
                if cap["evidence_class"] == "OFFLINE_POLICY_ONLY" and not code.startswith("tools/"):
                    raise ValueError("EVIDENCE_CLASS_REFERENCE_MISMATCH")
                if cap["evidence_class"] == "SYNTHETIC_INTERNAL" and not code.startswith("src/"):
                    raise ValueError("EVIDENCE_CLASS_REFERENCE_MISMATCH")
                result["references_to_test_only_or_disconnected_code"] += 1
            mapped[epic].append(cap)
        if set(seen) != {f"TRN-CAP-{n:02d}-{j:02d}"
                         for n in range(1, 14) for j in range(1, 4)}:
            raise ValueError("CANDIDATE_CAPABILITY_SCOPE_CHANGED")
        result["epic_count"] = len(mapped)
        result["provisional_capabilities"] = len(seen)
        result["external_blockers_unverified"] = len(byid)
        for epic in ALL_EPICS:
            caps = mapped[epic]
            result["epics"].append({
                "id": epic,
                "label": byid[epic].get("label", epic),
                "provisional_capabilities": len(caps),
                "linked_nonproduction_code_test_pairs": sum(c["evidence_class"] != "NO_EVIDENCE" for c in caps),
                "formally_accepted_capabilities_recorded": 0,
                "open_external_blockers": list(byid[epic]["missing_evidence"]),
                "formal_acceptance_status": "NOT_VERIFIED",
                "implementation_completion_pct": None,
            })
        result["status"] = "DRAFT_EVIDENCE_CATALOG_ONLY"
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        permitted = {
            "DUPLICATE_JSON_KEY", "NONFINITE_JSON", "INVALID_JSON", "MISSING_OR_UNSAFE_SOURCE",
            "MEASUREMENT_BASELINE_INVALID", "RELEASE_BLOCKERS_UNVERIFIED_REQUIRED",
            "ALL_NATIONAL_MARKETS_AND_EPICS_REQUIRED", "CANDIDATE_CAPABILITY_SCOPE_CHANGED",
            "INVALID_CAPABILITY_SCHEMA", "CAPABILITY_EPIC_ID_INVALID_OR_DUPLICATE",
            "UNVERIFIED_CAPABILITY_CANNOT_SELF_APPROVE", "CAPABILITY_PROVENANCE_MISMATCH",
            "CAPABILITY_EVIDENCE_CLASS_UNSUPPORTED", "CANNOT_COUNT_UNVERIFIED_CODE_EVIDENCE",
            "MISSING_CODE_TEST_REFERENCE_PAIR", "MISSING_OR_UNSAFE_EVIDENCE_REFERENCE",
            "UNSAFE_OR_UNMAPPED_EVIDENCE_REFERENCE",
            "EVIDENCE_CLASS_REFERENCE_MISMATCH",
        }
        result["errors"] = [str(error) if str(error) in permitted else "MEASUREMENT_UNVERIFIABLE"]
        result["epics"] = []
        result["epic_count"] = 0
        result["provisional_capabilities"] = 0
        result["references_to_test_only_or_disconnected_code"] = 0
        result["capabilities_without_code_test_links"] = 0
        result["external_blockers_unverified"] = 0
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.root.resolve())
    args.report_json.parent.mkdir(parents=True, exist_ok=True)
    args.report_json.write_text(json.dumps(report, indent=2, sort_keys=True, ensure_ascii=True) + "\n", encoding="utf-8")
    print("Epic measurement: " + report["status"] +
          "; epics=" + str(report["epic_count"]) +
          "; provisional_capabilities=" + str(report["provisional_capabilities"]) +
          "; implementation_pct=UNBASELINED; release_allowed=false.")
    if report["errors"]:
        print("DENIED: " + ", ".join(report["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
