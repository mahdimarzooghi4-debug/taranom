#!/usr/bin/env python3
"""TRN-TECH-161: synthetic *untrusted* key rotation/revocation claims, DENY only.

This is NOT an actual trust registry, key store, third-party custodian, signed
revocation certificate, replay database or Production policy. Any reference
or sequence number is a synthetic test comparison token, not proof of authority.
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
import worker_custodian_identity_boundary as identity

SCHEMA = 1
SCOPE = "TRN_TECH_161_UNTRUSTED_TRUST_KEY_LIFECYCLE"
MAX_BYTES = 8192
ROOT_FIELDS = {"schema_version", "scope", "candidate"}
CANDIDATE_FIELDS = {"events", "attempted_key_ref", "transfer_ref",
                    "challenge_ref"}
EVENT_FIELDS = {"sequence", "event_ref", "key_ref", "kind", "previous_key_ref"}
TEST_REF = re.compile(r"test:[A-Za-z0-9][A-Za-z0-9_-]{5,59}\Z")
KINDS = {"PRESENTED", "ROTATION_CLAIMED", "REVOKED", "RETIRED"}
CONTEXT_STATUSES = {"EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED",
                    "IDENTITY_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",
                    "DENIED_NO_AUTHENTICATED_OUTSIDE_RECEIPT"}
MAX_TEST_EVENTS = 6
FLAGS = (
    *identity.AUTHORITY_KEYS,
    "trust_key_lifecycle_verified", "rotation_approved",
    "revocation_proven", "revoked_key_reactivated", "retired_key_accepted"
)

def denied(status: str, prior_checked: bool = False) -> dict:
    out = {"schema_version": SCHEMA, "scope": SCOPE, "status": status,
           "synthetic_archive_shape_checked": prior_checked}
    out.update({k: False for k in FLAGS})
    return out

def prior_context_valid(ctx: dict) -> bool:
    return (
        type(ctx) is dict and set(ctx) == set(identity.denied("unused")) and
        type(ctx.get("schema_version")) is int and
        ctx["schema_version"] == identity.SCHEMA and
        ctx.get("scope") == identity.SCOPE and
        type(ctx.get("status")) is str and
        ctx["status"] in CONTEXT_STATUSES and
        ctx.get("synthetic_archive_shape_checked") is True and
        all(ctx.get(k) is False for k in identity.AUTHORITY_KEYS)
    )

def inspect(raw: bytes, identity_context: dict,
            *, expected_transfer: str = identity.TEST_TRANSFER,
            expected_challenge: str = identity.TEST_CHALLENGE) -> dict:
    """No claim transitions into trusted identity, key or release state."""
    if not prior_context_valid(identity_context):
        return denied("DENIED_UNTRUSTED_IDENTITY_CONTEXT")
    result = denied("DENIED_INVALID_KEY_LIFECYCLE_CLAIM", True)
    try:
        if type(raw) is not bytes or not 0 < len(raw) <= MAX_BYTES:
            return result
        data = json.loads(raw.decode("utf-8"),
                          object_pairs_hook=custody.unique,
                          parse_constant=custody.reject_constant)
        if (type(data) is not dict or set(data) != ROOT_FIELDS or
            type(data["schema_version"]) is not int or
            data["schema_version"] != SCHEMA or data["scope"] != SCOPE):
            return result
        candidate = data["candidate"]
        if candidate is None:
            result["status"] = "EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED"
            return result
        if type(candidate) is not dict or set(candidate) != CANDIDATE_FIELDS:
            return result
        if (any(type(candidate[k]) is not str or
                TEST_REF.fullmatch(candidate[k]) is None
                for k in ("attempted_key_ref", "transfer_ref", "challenge_ref")) or
            type(candidate["events"]) is not list or
            not 1 <= len(candidate["events"]) <= MAX_TEST_EVENTS):
            return result
        if (candidate["transfer_ref"] != expected_transfer or
            candidate["challenge_ref"] != expected_challenge):
            result["status"] = "DENIED_TRANSFER_OR_CHALLENGE_REPLAY"
            return result

        active = None
        excluded = set()
        event_ids = set()
        last_kind = ""
        for index, event in enumerate(candidate["events"], 1):
            if (type(event) is not dict or set(event) != EVENT_FIELDS or
                type(event["sequence"]) is not int or
                event["sequence"] != index or
                type(event["kind"]) is not str or
                event["kind"] not in KINDS or
                any(type(event[k]) is not str or
                    TEST_REF.fullmatch(event[k]) is None
                    for k in ("event_ref", "key_ref")) or
                (event["previous_key_ref"] is not None and
                 (type(event["previous_key_ref"]) is not str or
                  TEST_REF.fullmatch(event["previous_key_ref"]) is None))):
                return result
            if event["event_ref"] in event_ids:
                result["status"] = "DENIED_REPLAYED_EVENT"
                return result
            event_ids.add(event["event_ref"])
            key = event["key_ref"]
            kind = event["kind"]
            previous = event["previous_key_ref"]
            if index == 1:
                if kind != "PRESENTED" or previous is not None:
                    result["status"] = "DENIED_INVALID_INITIAL_CLAIM"
                    return result
                active = key
            elif active is None:
                result["status"] = "DENIED_REACTIVATION_AFTER_TERMINATION"
                return result
            elif kind == "ROTATION_CLAIMED":
                if previous != active or key == active or key in excluded:
                    result["status"] = "DENIED_INCONSISTENT_ROTATION_CLAIM"
                    return result
                excluded.add(active)
                active = key
            elif kind in ("REVOKED", "RETIRED"):
                if previous is not None or key != active:
                    result["status"] = "DENIED_INCONSISTENT_TERMINATION_CLAIM"
                    return result
                excluded.add(active)
                active = None
            else:
                result["status"] = "DENIED_REPLAYED_OR_REACTIVATED_KEY_CLAIM"
                return result
            last_kind = kind

        attempted = candidate["attempted_key_ref"]
        if attempted in excluded and last_kind in ("REVOKED", "RETIRED"):
            result["status"] = "DENIED_REVOKED_OR_RETIRED_KEY"
        elif attempted in excluded:
            result["status"] = "DENIED_SUPERSEDED_KEY"
        elif active is None:
            result["status"] = "DENIED_KEY_LIFECYCLE_TERMINATED"
        elif attempted != active:
            result["status"] = "DENIED_UNKNOWN_OR_STALE_KEY"
        else:
            # A syntactically consistent *assertion* is never acceptance.
            result["status"] = "KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW"
        return result
    except (ValueError,TypeError,KeyError,UnicodeError,
            custody.InvalidCustodyContract,json.JSONDecodeError):
        return result

def _template(path: Path, expected_name: str) -> bytes:
    expected = Path(__file__).resolve().parents[2] / "docs/governance" / expected_name
    if path.is_symlink() or path.absolute() != expected:
        raise ValueError("TEST_TEMPLATE_ONLY")
    return custody._secure_input(path)

def main():
    parser = argparse.ArgumentParser(description="CI-only denial of unsupported key lifecycle")
    parser.add_argument("--lifecycle-template",required=True)
    parser.add_argument("--identity-template",required=True)
    parser.add_argument("--receipt-template",required=True)
    parser.add_argument("--report-json",required=True)
    args = parser.parse_args()
    try:
        if not all(os.environ.get(k) == "CI_EPHEMERAL_ONLY" for k in (
            "TARANOM_CENTER_DEMO_TEST_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")):
            raise ValueError("TEST_ONLY")
        archive_dir = handoff.directory()
        packed = anchor.checked_file(archive_dir / handoff.ZIP_NAME,
                                     handoff.ZIP_MAX)
        receipt_result = receipt.inspect(packed, _template(
            Path(args.receipt_template), "ai-worker-external-receipt-template.json"))
        if receipt_result["status"] != "EXTERNAL_RECEIPT_NOT_SUPPLIED":
            raise ValueError("NO_EXTERNAL_RECEIPT_ALLOWED")
        identity_result = identity.inspect(_template(
            Path(args.identity_template), "ai-worker-custodian-identity-template.json"),
            receipt_result)
        if identity_result["status"] != "EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED":
            raise ValueError("NO_REAL_IDENTITY_ALLOWED")
        result = inspect(_template(Path(args.lifecycle_template),
            "ai-worker-trust-key-lifecycle-template.json"), identity_result)
        if result["status"] != "EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED":
            raise ValueError("NO_REAL_LIFECYCLE_ALLOWED")
        custody._write_denial_only(Path(args.report_json).absolute(), result)
        print("PASS: no external Worker trust-key lifecycle or authorization after real synthetic restart")
    except (OSError,ValueError,TypeError,KeyError):
        print("WORKER_TRUST_KEY_LIFECYCLE_DENIED",file=sys.stderr)
        raise SystemExit(17) from None

if __name__ == "__main__":
    main()
