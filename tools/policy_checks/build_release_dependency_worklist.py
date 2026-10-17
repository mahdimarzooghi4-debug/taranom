#!/usr/bin/env python3
"""TRN-TECH-164: derive a traceable NON-authorizing delivery dependency worklist.

Never invent business owners, rank decisions, readiness percentages or turn
the existence of synthetic test code into formally accepted product progress.
Use only two existing, independently checked on-repository source registers.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path
import sys

import check_epic_capability_progress as epic
import check_national_launch_blockers as national

STATUS = "DRAFT_UNVERIFIED_DEPENDENCY_WORKLIST"
DENIED = "DENIED_UNVERIFIABLE_DEPENDENCY_SOURCES"
SCOPE = "COMPREHENSIVE_NATIONAL_DEPENDENCY_WORKLIST"
BLOCKER_ROOT = "docs/governance/national-launch-blockers.json"
CAPABILITY_ROOT = "docs/governance/epic-capability-progress-draft.json"

def empty():
    return {
        "schema_version": 1, "scope": SCOPE, "status": DENIED,
        "release_admission": "DENIED_BY_DEFAULT",
        "formal_approval_observed": False,
        "production_ready": False, "national_release_allowed": False,
        "blocked_domains": 0, "candidate_capabilities": 0,
        "required_owners_unassigned": 0,
        "source_fingerprints_sha256": {},
        "work_items": [],
    }

def inspect(root: Path) -> dict:
    report=empty()
    try:
        # Neither source validator can emit a positive Production decision.
        progress=epic.inspect(root)
        if (progress["status"] != "DRAFT_EVIDENCE_CATALOG_ONLY" or
            progress["epic_count"] != 13 or
            progress["provisional_capabilities"] != 39 or
            progress["external_blockers_unverified"] != 17 or
            progress["formally_accepted_capabilities_recorded"] != 0 or
            progress["overall_implementation_completion_pct"] is not None or
            progress["formal_acceptance_pct"] is not None or
            progress["baseline_approved"] is not False or
            progress["national_release_allowed"] is not False):
            raise ValueError("INVALID_EPIC_BASELINE")
        blocked=epic._read(root / epic.BLOCKERS)
        capability=epic._read(root / epic.REGISTRY)
        if national.audit_inventory(root, blocked):
            raise ValueError("INVALID_NATIONAL_BLOCKERS")
        if (len(blocked["items"]) != 17 or
            len(capability["capabilities"]) != 39 or
            set(x["id"] for x in blocked["items"]) != set(national.REQUIRED)):
            raise ValueError("SOURCE_CARDINALITY_DRIFT")

        refs_by_epic={}
        for item in capability["capabilities"]:
            refs_by_epic.setdefault(item["epic_id"],[]).append(item)
        domains={}
        for item in blocked["items"]:
            for requirement in item["missing_evidence"]:
                if requirement not in national.ALLOWED_BLOCKERS:
                    raise ValueError("UNEXPECTED_BLOCKER")
                domains.setdefault(requirement,[]).append(item)

        if set(domains) != set(national.ALLOWED_BLOCKERS):
            # Refuse silently dropping any of the 18 documented dependencies.
            raise ValueError("REQUIRED_WORK_CATEGORY_MISSING")
        compiled=[]
        for requirement in sorted(domains):
            impacted=sorted(domains[requirement],key=lambda d:d["id"])
            epics=sorted(d["id"] for d in impacted if d["id"].startswith("TRN-EP-"))
            market_scopes=sorted(d["id"] for d in impacted if not d["id"].startswith("TRN-EP-"))
            decisions=sorted(set(ref for d in impacted for ref in d["policy_refs"]))
            # Only count existing NON-production source code+test references.
            # Duplicates here mean per-Epic candidate references, not acceptance.
            evidence_pairs=sum(
                1 for eid in epics for cap in refs_by_epic[eid]
                if cap["evidence_class"] != "NO_EVIDENCE"
            )
            compiled.append({
                "requirement": requirement,
                "blocked_domain_ids": [d["id"] for d in impacted],
                "affected_epic_ids": epics,
                "affected_market_or_national_ids": market_scopes,
                "policy_refs": decisions,
                "linked_nonproduction_candidate_pairs_in_affected_epics": evidence_pairs,
                "decision_owner": "NOT_ASSIGNED",
                "evidence_status": "BLOCKED_UNVERIFIED",
                "human_decision_status": "NOT_OBSERVED",
                "formal_acceptance_observed": False,
            })
        report.update({
            "status": STATUS,
            "blocked_domains": 17,
            "candidate_capabilities": 39,
            "required_owners_unassigned": len(compiled),
            "source_fingerprints_sha256": {
                BLOCKER_ROOT: hashlib.sha256((root / BLOCKER_ROOT).read_bytes()).hexdigest(),
                CAPABILITY_ROOT: hashlib.sha256((root / CAPABILITY_ROOT).read_bytes()).hexdigest(),
            },
            "work_items": compiled,
        })
    except (OSError,ValueError,TypeError,KeyError,AttributeError):
        return empty()
    return report

def markdown_report(r):
    if r["status"] != STATUS:
        raise ValueError("INVALID_REPORT")
    lines=[
        "# ترنم — صف وابستگی‌های باز برای عرضه جامع و سراسری",
        "",
        "**DRAFT — فقط استخراج از دو فهرست موجود در مخزن؛ نه تعیین متولی، اولویت، درصد پیشرفت یا مجوز انتشار.**",
        "",
        f"- حوزه‌های مسدود: {r['blocked_domains']} (۱۳ Epic + سه بازار + تعهد پوشش ملی)",
        f"- دسته‌های مدرک/تصمیم نیازمند بررسی مستقل: {len(r['work_items'])}",
        f"- قابلیت‌های پیشنهادی، نه پذیرفته‌شده: {r['candidate_capabilities']}",
        "- تأیید بیرونی، مالک پاسخ‌گو و پذیرش رسمی هیچ‌یک احراز نشده‌اند.",
        "- Stage/QA/Release/Production/National: **DENIED_BY_DEFAULT**",
        "",
        "| دسته مدرک یا تصمیم باز | حوزه‌های متأثر | اسناد Business مرجع |",
        "|---|---|---|",
    ]
    for item in r["work_items"]:
        lines.append("| `"+item["requirement"]+"` | "+
                     ", ".join(item["blocked_domain_ids"])+" | "+
                     ", ".join(item["policy_refs"])+" |")
    lines += ["", "## نیازمند تعیین انسانی (بدون انتخاب خودکار)",
        "متولی قانونی و فنی واقعی، مرجع مدرک مستقل، حدود مجوز داده/خدمت، قرارداد یا صلاحیت مرتبط و مرجع بازبینی/تصمیم برای هر مورد از منابع واقعی باید تعیین و مستند شوند.",
        "وجود پیوند کد/تست، موفقیت CI یا ادعای خوداظهاری، هیچ موردی را VERIFIED یا READY نمی‌کند.",
        "", "## اثر انگشت منابع"]
    for key,value in sorted(r["source_fingerprints_sha256"].items()):
        lines.append("- `"+key+"`: `"+value+"`")
    return "\n".join(lines)+"\n"

def csv_report(r):
    if r["status"]!=STATUS:
        raise ValueError("INVALID_REPORT")
    buff=io.StringIO()
    writer=csv.writer(buff,lineterminator="\n")
    writer.writerow(("requirement","blocked_domains","affected_epics",
        "affected_markets","policy_refs","owner","evidence_status",
        "formal_acceptance_observed","national_release_allowed"))
    for item in r["work_items"]:
        writer.writerow((item["requirement"],
            ";".join(item["blocked_domain_ids"]),
            ";".join(item["affected_epic_ids"]),
            ";".join(item["affected_market_or_national_ids"]),
            ";".join(item["policy_refs"]),
            item["decision_owner"],item["evidence_status"],"false","false"))
    return buff.getvalue()

def write_new(path:Path,content:str):
    if path.exists() or path.is_symlink() or path.parent.is_symlink():
        raise ValueError("REPORT_CANNOT_REPLACE_PRIOR_EVIDENCE")
    if not path.parent.is_dir():
        raise ValueError("REPORT_PARENT_MISSING")
    with path.open("x",encoding="utf-8") as stream:
        stream.write(content)

def main():
    cli=argparse.ArgumentParser(description="Offline release blockers/decisions dependency view")
    cli.add_argument("--root",type=Path,default=Path("."))
    cli.add_argument("--report-json",type=Path,required=True)
    cli.add_argument("--report-md",type=Path,required=True)
    cli.add_argument("--report-csv",type=Path,required=True)
    args=cli.parse_args()
    try:
        root=args.root.resolve(strict=True)
        report=inspect(root)
        if report["status"]!=STATUS:
            raise ValueError("INVALID_DEPENDENCIES")
        targets=[x.absolute() for x in (args.report_json,args.report_md,args.report_csv)]
        if len(set(targets))!=3 or any(p.exists() or p.is_symlink() for p in targets):
            raise ValueError("REPORT_TARGETS_UNSAFE")
        write_new(targets[0],json.dumps(report,indent=2,sort_keys=True)+"\n")
        write_new(targets[1],markdown_report(report))
        write_new(targets[2],csv_report(report))
        print("PASS: 17 blocked domains, 18 unowned evidence requirements; national release DENIED")
    except (OSError,ValueError,TypeError,KeyError):
        print("DENIED: source-backed release dependency worklist invalid",file=sys.stderr)
        raise SystemExit(17) from None

if __name__=="__main__":
    main()
