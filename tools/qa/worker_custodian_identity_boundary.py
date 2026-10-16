#!/usr/bin/env python3
"""TRN-TECH-160: custodian identity and trust-root CLAIMS are never authority.

No independent organization, approved trust anchor, protocol, real key, legal
custodian, signed challenge or production identity has been chosen.
This module only tests synthetic anti-spoof boundaries and always denies
positive identity/receipt verification; no network or key material.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import sys

import check_worker_external_custody as custody
import worker_evidence_anchor as anchor
import worker_evidence_handoff as handoff
import worker_external_receipt_denial as receipt

SCHEMA = 1
SCOPE = "TRN_TECH_160_UNTRUSTED_CUSTODIAN_IDENTITY"
MAX_INPUT = 4096
ROOT_FIELDS = {"schema_version","scope","candidate"}
CLAIM_FIELDS = {
    "custodian_ref","administrative_domain_ref","trust_key_ref",
    "reviewer_ref","transfer_ref","challenge_ref","claimed_status"
}
TEST_REF = re.compile(r"test:[A-Za-z0-9][A-Za-z0-9_-]{5,59}\Z")
# These are fixed *synthetic comparison fixtures*, NOT trusted Production
# keys, registry entries, approved custodians or replay protection.
TEST_DOMAIN = "test:expected-admin-boundary-one"
TEST_KEY = "test:expected-key-reference-one"
TEST_CHALLENGE = "test:expected-request-challenge-one"
TEST_TRANSFER = receipt.EXPECTED_TEST_TRANSFER
RECEIPT_STATUSES = {"EXTERNAL_RECEIPT_NOT_SUPPLIED",
                    "STRUCTURAL_MATCH_UNAUTHENTICATED"}
AUTHORITY_KEYS = (
    "custodian_identity_verified","separate_admin_domain_verified",
    "trust_anchor_verified","external_receipt_verified",
    "independent_custody_verified","human_release_approved",
    "production_ready","national_release_allowed"
)

def denied(status: str, shape_checked: bool = False) -> dict:
    data = {
        "schema_version": SCHEMA,
        "scope": SCOPE,
        "status": status,
        "synthetic_archive_shape_checked": shape_checked,
    }
    data.update({name: False for name in AUTHORITY_KEYS})
    return data

def receipt_context_valid(context: dict) -> bool:
    if type(context) is not dict or set(context) != set(receipt.denied("invalid")):
        return False
    if (context.get("schema_version") != receipt.SCHEMA or
        type(context.get("schema_version")) is not int or
        context.get("scope") != receipt.SCOPE or
        context.get("status") not in RECEIPT_STATUSES or
        context.get("internal_archive_shape_checked") is not True):
        return False
    return all(context.get(k) is False for k in (
        "external_receipt_verified","independent_custody_verified",
        "real_custodian_identity_verified",
        "retention_and_privacy_authorized","human_release_approved",
        "production_ready","national_release_allowed"
    ))

def inspect(untrusted: bytes, receipt_context: dict,
            *, expected_domain: str = TEST_DOMAIN,
            expected_key: str = TEST_KEY,
            expected_challenge: str = TEST_CHALLENGE,
            expected_transfer: str = TEST_TRANSFER) -> dict:
    """Only compares synthetic fields after full receipt-context DENIAL.

    All caller-provided refs are self-assertions. There is no positive branch.
    """
    if not receipt_context_valid(receipt_context):
        return denied("DENIED_UNTRUSTED_RECEIPT_CONTEXT")
    result = denied("DENIED_INVALID_IDENTITY_CLAIM", True)
    try:
        if type(untrusted) is not bytes or not 0 < len(untrusted) <= MAX_INPUT:
            return result
        value = json.loads(untrusted.decode("utf-8"),
                           object_pairs_hook=custody.unique,
                           parse_constant=custody.reject_constant)
        if (type(value) is not dict or set(value) != ROOT_FIELDS or
            type(value["schema_version"]) is not int or
            value["schema_version"] != SCHEMA or value["scope"] != SCOPE):
            return result
        claim = value["candidate"]
        if claim is None:
            result["status"] = "EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED"
            return result
        if type(claim) is not dict or set(claim) != CLAIM_FIELDS:
            return result
        if (any(type(claim[k]) is not str or TEST_REF.fullmatch(claim[k]) is None
                for k in CLAIM_FIELDS - {"claimed_status"}) or
            claim["claimed_status"] != "PRESENTED"):
            return result
        if claim["transfer_ref"] != expected_transfer:
            result["status"] = "DENIED_TRANSFER_SCOPE_OR_REPLAY"
        elif claim["challenge_ref"] != expected_challenge:
            result["status"] = "DENIED_CHALLENGE_REPLAY_OR_MISMATCH"
        elif claim["administrative_domain_ref"] != expected_domain:
            result["status"] = "DENIED_ADMIN_DOMAIN_MISMATCH"
        elif claim["trust_key_ref"] != expected_key:
            result["status"] = "DENIED_KEY_REFERENCE_MISMATCH"
        elif receipt_context["status"] == "EXTERNAL_RECEIPT_NOT_SUPPLIED":
            result["status"] = "DENIED_NO_AUTHENTICATED_OUTSIDE_RECEIPT"
        else:
            # Matching string claims DO NOT authenticate an organization,
            # possession of a key, separate admin domain, or actual reviewer.
            result["status"] = "IDENTITY_CLAIMS_REQUIRE_INDEPENDENT_REVIEW"
        return result
    except (ValueError,TypeError,KeyError,UnicodeError,
            custody.InvalidCustodyContract,json.JSONDecodeError):
        return result

def _template(path: Path, expected_name: str) -> bytes:
    expected = Path(__file__).resolve().parents[2] / "docs" / "governance" / expected_name
    if path.is_symlink() or path.absolute() != expected:
        raise ValueError("TEST_TEMPLATE_ONLY")
    return custody._secure_input(path)

def main():
    parser = argparse.ArgumentParser(description="CI-only nonauthorizing identity boundary")
    parser.add_argument("--identity-template",required=True)
    parser.add_argument("--receipt-template",required=True)
    parser.add_argument("--report-json",required=True)
    args = parser.parse_args()
    try:
        if not all(os.environ.get(k) == "CI_EPHEMERAL_ONLY" for k in (
            "TARANOM_CENTER_DEMO_TEST_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")):
            raise ValueError("TEST_ONLY")
        archive_folder = handoff.directory()
        archive = anchor.checked_file(archive_folder/handoff.ZIP_NAME,
                                      handoff.ZIP_MAX)
        raw_receipt = _template(Path(args.receipt_template),
                                "ai-worker-external-receipt-template.json")
        context = receipt.inspect(archive,raw_receipt)
        if context["status"] != "EXTERNAL_RECEIPT_NOT_SUPPLIED":
            raise ValueError("UNTRUSTED_RECEIPT_IN_CI")
        raw_identity = _template(Path(args.identity_template),
                                 "ai-worker-custodian-identity-template.json")
        out = inspect(raw_identity,context)
        if out["status"] != "EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED":
            raise ValueError("UNTRUSTED_IDENTITY_CLAIM_IN_CI")
        custody._write_denial_only(Path(args.report_json).absolute(),out)
        print("PASS: real synthetic Worker archive has no external custodian identity or trusted receipt")
    except (OSError,ValueError,TypeError,KeyError):
        print("WORKER_CUSTODIAN_IDENTITY_DENIED",file=sys.stderr)
        raise SystemExit(17) from None

if __name__ == "__main__":
    main()
