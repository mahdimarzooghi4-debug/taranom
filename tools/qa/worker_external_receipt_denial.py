#!/usr/bin/env python3
"""TRN-TECH-159: DENIAL-ONLY structural check of untrusted outside receipt claims.

No outside issuer, signing key, identifier, receipt protocol, transmission
service, legal custodian or acceptance decision has been authorized.
Even a structurally matching claim ALWAYS remains unverified.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys

import check_worker_external_custody as custody
import worker_evidence_anchor as anchor
import worker_evidence_handoff as handoff
from verify_ai_worker_evidence import verify_bytes

SCHEMA = 1
SCOPE = "TRN_TECH_159_UNTRUSTED_EXTERNAL_RECEIPT_REHEARSAL"
INPUT_LIMIT = 4096
FIELDS = {"schema_version", "scope", "claim"}
CLAIM_FIELDS = {"transfer_ref", "archive_sha256", "manifest_sha256",
                "ledger_head_digest", "anchored_count", "claimed_disposition"}
HEX64 = re.compile(r"[0-9A-F]{64}\Z")
REFERENCE = re.compile(r"test:[A-Za-z0-9][A-Za-z0-9_-]{5,59}\Z")
EXPECTED_TEST_TRANSFER = "test:worker-synthetic-handoff-one"

def denied(status):
    return {
        "schema_version": SCHEMA, "scope": SCOPE, "status": status,
        "internal_archive_shape_checked": False,
        "external_receipt_verified": False,
        "independent_custody_verified": False,
        "real_custodian_identity_verified": False,
        "retention_and_privacy_authorized": False,
        "human_release_approved": False,
        "production_ready": False,
        "national_release_allowed": False,
    }

def inspect(archive, untrusted_claim, expected_transfer=EXPECTED_TEST_TRANSFER):
    """Pure test-only structural evaluation. No successful authority result."""
    result=denied("DENIED_INVALID_RECEIPT_CLAIM")
    try:
        if (type(untrusted_claim) is not bytes or
            not 0<len(untrusted_claim)<=INPUT_LIMIT or
            type(archive) is not bytes or not archive):
            return result
        candidate=json.loads(untrusted_claim.decode("utf-8"),
                             object_pairs_hook=custody.unique,
                             parse_constant=custody.reject_constant)
        if (type(candidate) is not dict or set(candidate)!=FIELDS or
            type(candidate["schema_version"]) is not int or
            candidate["schema_version"]!=SCHEMA or
            candidate["scope"]!=SCOPE):
            return result
        # First validate the ARCHIVE independently of external assertions.
        # These are internal synthetic checks, not a trusted custody proof.
        try:
            ledger,raw_manifest=handoff.open_zip(archive)
            manifest=handoff.decode_manifest(raw_manifest)
            rows,boots,last=verify_bytes(ledger)
            n=manifest["anchoredCount"]
            if (len(boots)!=2 or len(rows)!=manifest["recordCount"] or
                len(rows)!=n+2 or n<3 or last!=manifest["lastEpoch"] or
                boots[0]!=manifest["anchoredEpoch"] or
                boots[1]==boots[0] or
                handoff.sha(ledger)!=manifest["ledgerSha256"] or
                handoff.sha(("\n".join(rows[:n])+"\n").encode())!=
                manifest["anchorPrefixSha256"] or
                json.loads(rows[n-1])["digest"]!=manifest["anchoredDigest"] or
                json.loads(rows[n])["kind"]!="Boot" or
                json.loads(rows[n])["previousDigest"]!=manifest["anchoredDigest"] or
                json.loads(rows[n+1])["kind"]!="Success" or
                json.loads(rows[-1])["digest"]!=manifest["headDigest"]):
                result["status"]="DENIED_INVALID_SYNTHETIC_ARCHIVE"
                return result
        except (AssertionError,ValueError,TypeError,KeyError,IndexError,
                UnicodeError,EOFError):
            result["status"]="DENIED_INVALID_SYNTHETIC_ARCHIVE"
            return result
        result["internal_archive_shape_checked"]=True
        claim=candidate["claim"]
        if claim is None:
            result["status"]="EXTERNAL_RECEIPT_NOT_SUPPLIED"
            return result
        if type(claim) is not dict or set(claim)!=CLAIM_FIELDS:
            return result
        transfer=claim["transfer_ref"]
        if (type(transfer) is not str or REFERENCE.fullmatch(transfer) is None or
            any(type(claim[k]) is not str or HEX64.fullmatch(claim[k]) is None
                for k in ("archive_sha256","manifest_sha256","ledger_head_digest")) or
            type(claim["anchored_count"]) is not int or
            not 0<claim["anchored_count"]<=4096 or
            claim["claimed_disposition"]!="RECEIVED"):
            return result
        if transfer!=expected_transfer:
            result["status"]="DENIED_RECEIPT_REPLAY_OR_SCOPE"
        elif claim["archive_sha256"]!=handoff.sha(archive):
            result["status"]="DENIED_RECEIPT_ARCHIVE_MISMATCH"
        elif claim["manifest_sha256"]!=handoff.sha(raw_manifest):
            result["status"]="DENIED_RECEIPT_MANIFEST_MISMATCH"
        elif claim["ledger_head_digest"]!=manifest["headDigest"]:
            result["status"]="DENIED_RECEIPT_HEAD_MISMATCH"
        elif claim["anchored_count"]!=n:
            result["status"]="DENIED_RECEIPT_ANCHOR_MISMATCH"
        else:
            # This is not a signature; both claim and reference are
            # self-asserted synthetic test data, with no trusted issuer.
            result["status"]="STRUCTURAL_MATCH_UNAUTHENTICATED"
        return result
    except (custody.InvalidCustodyContract,json.JSONDecodeError,UnicodeError,
            KeyError,TypeError,ValueError):
        return result

def main():
    parser=argparse.ArgumentParser(description="CI-only negative external receipt claims")
    parser.add_argument("--claim-template",required=True)
    parser.add_argument("--report-json",required=True)
    args=parser.parse_args()
    try:
        if not all(__import__("os").environ.get(key)=="CI_EPHEMERAL_ONLY" for key
                in ("TARANOM_CENTER_DEMO_TEST_ONLY",
                    "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")):
            raise ValueError("CI_ONLY")
        expected=Path(__file__).resolve().parents[2] / "docs/governance/ai-worker-external-receipt-template.json"
        supplied=Path(args.claim_template)
        if supplied.is_symlink() or supplied.absolute()!=expected:
            raise ValueError("TEMPLATE_ONLY")
        archive_dir=handoff.directory()
        raw=anchor.checked_file(archive_dir/handoff.ZIP_NAME,handoff.ZIP_MAX)
        claim=custody._secure_input(supplied)
        report=inspect(raw,claim)
        if report["status"]!="EXTERNAL_RECEIPT_NOT_SUPPLIED":
            raise ValueError("UNAUTHORIZED_EXTERNAL_CLAIM_IN_CI")
        custody._write_denial_only(Path(args.report_json).absolute(),report)
        print("PASS: actual two-epoch synthetic archive has no authenticated external receipt")
    except (OSError,ValueError,KeyError,TypeError):
        print("WORKER_EXTERNAL_RECEIPT_DENIED",file=sys.stderr)
        raise SystemExit(17) from None

if __name__=="__main__":
    main()
