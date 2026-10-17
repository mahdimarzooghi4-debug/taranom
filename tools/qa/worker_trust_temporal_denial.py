#!/usr/bin/env python3
"""TRN-TECH-162: denial-only synthetic key-time and revocation-order claims.

No trusted time source, signed timestamp, validity duration, clock tolerance,
real key, independent revocation feed or Production policy is configured.
All comparison timestamps are strictly formatted, claimant-controlled test
fixtures. Comparison with system time is intentionally FORBIDDEN.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
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
import worker_trust_key_lifecycle_denial as lifecycle

SCHEMA = 1
SCOPE = "TRN_TECH_162_UNTRUSTED_TRUST_TEMPORAL_CLAIMS"
MAX_BYTES = 12000
ROOT_FIELDS = {"schema_version", "scope", "candidate"}
CLAIM_FIELDS = {"attempted_key_ref", "attempted_at", "transfer_ref",
                "challenge_ref", "key_windows", "events"}
WINDOW_FIELDS = {"key_ref", "claimed_not_before", "claimed_expires_at"}
EVENT_FIELDS = lifecycle.EVENT_FIELDS | {"claimed_effective_at", "claimed_observed_at"}
UTC_SHAPE = re.compile(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\Z")
PREV_STATES = {"EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED",
               "KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",
               "DENIED_REVOKED_OR_RETIRED_KEY", "DENIED_SUPERSEDED_KEY",
               "DENIED_KEY_LIFECYCLE_TERMINATED"}
FLAGS = (*lifecycle.FLAGS, "trusted_clock_verified", "validity_window_authorized",
         "expiration_verified", "revocation_time_authenticated",
         "time_order_attested", "external_timestamp_signed")

def denied(status: str, checked: bool = False):
    out = {"schema_version": SCHEMA, "scope": SCOPE, "status": status,
           "synthetic_archive_shape_checked": checked}
    out.update({name: False for name in FLAGS})
    return out

def prior_valid(ctx):
    return (type(ctx) is dict and set(ctx) == set(lifecycle.denied("unused")) and
            type(ctx.get("schema_version")) is int and
            ctx["schema_version"] == lifecycle.SCHEMA and
            ctx.get("scope") == lifecycle.SCOPE and
            type(ctx.get("status")) is str and ctx["status"] in PREV_STATES and
            ctx.get("synthetic_archive_shape_checked") is True and
            all(ctx.get(k) is False for k in lifecycle.FLAGS))

def instant(value):
    """UTC parser for a test fixture; not an authoritative time statement."""
    if type(value) is not str or UTC_SHAPE.fullmatch(value) is None:
        raise ValueError("UNSUPPORTED_TIME_REPRESENTATION")
    parsed = datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
    if parsed.strftime("%Y-%m-%dT%H:%M:%SZ") != value:
        raise ValueError("NON_CANONICAL_UTC")
    return parsed

def inspect(raw: bytes, prior: dict, *,
            expected_transfer: str = identity.TEST_TRANSFER,
            expected_challenge: str = identity.TEST_CHALLENGE):
    """A complete and coherent clock claim is NEVER acceptance."""
    if not prior_valid(prior):
        return denied("DENIED_UNTRUSTED_KEY_LIFECYCLE_CONTEXT")
    result = denied("DENIED_INVALID_TEMPORAL_CLAIM", True)
    try:
        if type(raw) is not bytes or not 0 < len(raw) <= MAX_BYTES:
            return result
        value = json.loads(raw.decode("utf-8"),object_pairs_hook=custody.unique,
                           parse_constant=custody.reject_constant)
        if (type(value) is not dict or set(value) != ROOT_FIELDS or
            type(value["schema_version"]) is not int or
            value["schema_version"] != SCHEMA or value["scope"] != SCOPE):
            return result
        candidate=value["candidate"]
        if candidate is None:
            result["status"] = "TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED"
            return result
        if type(candidate) is not dict or set(candidate) != CLAIM_FIELDS:
            return result
        if (any(type(candidate[k]) is not str or
                lifecycle.TEST_REF.fullmatch(candidate[k]) is None
                for k in ("attempted_key_ref", "transfer_ref", "challenge_ref")) or
            type(candidate["events"]) is not list or
            not 1 <= len(candidate["events"]) <= lifecycle.MAX_TEST_EVENTS or
            type(candidate["key_windows"]) is not list or
            not 1 <= len(candidate["key_windows"]) <= lifecycle.MAX_TEST_EVENTS):
            return result
        attempted=instant(candidate["attempted_at"])
        if (candidate["transfer_ref"] != expected_transfer or
            candidate["challenge_ref"] != expected_challenge):
            result["status"]="DENIED_TEMPORAL_TRANSFER_OR_CHALLENGE_REPLAY"
            return result
        windows={}
        for window in candidate["key_windows"]:
            if (type(window) is not dict or set(window)!=WINDOW_FIELDS or
                type(window["key_ref"]) is not str or
                lifecycle.TEST_REF.fullmatch(window["key_ref"]) is None):
                return result
            key=window["key_ref"]
            if key in windows:
                result["status"]="DENIED_DUPLICATE_TIME_WINDOW"
                return result
            start=instant(window["claimed_not_before"])
            end=instant(window["claimed_expires_at"])
            if start>=end:
                result["status"]="DENIED_INVALID_VALIDITY_ORDER"
                return result
            windows[key]=(start,end)

        stripped=[]
        introduced=set()
        prev_observed=None
        prev_effective=None
        prior_recorded=None
        for index,event in enumerate(candidate["events"],1):
            if type(event) is not dict or set(event)!=EVENT_FIELDS:
                return result
            stripped.append({k:event[k] for k in lifecycle.EVENT_FIELDS})
            # Original lifecycle evaluator is the canonical checker for event
            # ordering, predecessor lineage, repeated events and revocation.
            effective=instant(event["claimed_effective_at"])
            observed=instant(event["claimed_observed_at"])
            if effective>observed:
                result["status"]="DENIED_FUTURE_EFFECTIVE_TIME_CLAIM"
                return result
            if prev_observed is not None and observed<=prev_observed:
                result["status"]="DENIED_EVENT_OBSERVATION_ORDER"
                return result
            if prev_effective is not None and effective<prev_effective:
                result["status"]="DENIED_EVENT_EFFECTIVE_TIME_ORDER"
                return result
            # A retroactive termination may require a real legal decision.
            # Deny the claim rather than silently rewriting earlier history.
            if (event["kind"] in ("REVOKED","RETIRED") and
                prior_recorded is not None and effective<prior_recorded):
                result["status"]="DENIED_BACKDATED_TERMINATION_CLAIM"
                return result
            if event["kind"] in ("PRESENTED","ROTATION_CLAIMED"):
                introduced.add(event["key_ref"])
            prior_recorded=observed
            prev_observed=observed
            prev_effective=effective

        if set(windows)!=introduced:
            result["status"]="DENIED_MISSING_OR_UNKNOWN_KEY_WINDOW"
            return result
        # Binding the new temporal claim to the *existing* synthetic
        # lifecycle grammar does not make its statements authentic.
        prior_identity=identity.denied("EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED",True)
        lifecycle_raw=anchor.canonical({
            "schema_version":lifecycle.SCHEMA,"scope":lifecycle.SCOPE,
            "candidate":{"events":stripped,
                         "attempted_key_ref":candidate["attempted_key_ref"],
                         "transfer_ref":candidate["transfer_ref"],
                         "challenge_ref":candidate["challenge_ref"]}})
        life=lifecycle.inspect(lifecycle_raw,prior_identity)
        if life["status"] not in (
            "KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",
            "DENIED_REVOKED_OR_RETIRED_KEY", "DENIED_SUPERSEDED_KEY",
            "DENIED_KEY_LIFECYCLE_TERMINATED"):
            result["status"]="DENIED_INCONSISTENT_EVENT_LIFECYCLE"
            return result
        key=candidate["attempted_key_ref"]
        if key not in windows:
            result["status"]="DENIED_UNKNOWN_TEMPORAL_KEY"
        elif attempted<windows[key][0]:
            result["status"]="DENIED_BEFORE_CLAIMED_VALIDITY"
        elif attempted>=windows[key][1]:
            result["status"]="DENIED_AFTER_CLAIMED_EXPIRY"
        elif attempted<prev_observed:
            result["status"]="DENIED_ATTEMPT_PRECEDES_OBSERVED_HISTORY"
        elif life["status"]!="KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW":
            result["status"]="DENIED_TERMINATED_OR_SUPERSEDED_KEY"
        else:
            result["status"]="TEMPORAL_CLAIMS_REQUIRE_INDEPENDENT_REVIEW"
        return result
    except (ValueError,TypeError,KeyError,OverflowError,UnicodeError,
            custody.InvalidCustodyContract,json.JSONDecodeError):
        return result

def _template(path, name):
    expected=Path(__file__).resolve().parents[2]/"docs/governance"/name
    if path.is_symlink() or path.absolute()!=expected:
        raise ValueError("TEST_ONLY_TEMPLATE")
    return custody._secure_input(path)

def main():
    cli=argparse.ArgumentParser(description="CI-only untrusted time-claim boundary")
    cli.add_argument("--temporal-template",required=True)
    cli.add_argument("--lifecycle-template",required=True)
    cli.add_argument("--identity-template",required=True)
    cli.add_argument("--receipt-template",required=True)
    cli.add_argument("--report-json",required=True)
    args=cli.parse_args()
    try:
        if not all(os.environ.get(k)=="CI_EPHEMERAL_ONLY" for k in (
            "TARANOM_CENTER_DEMO_TEST_ONLY",
            "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")):
            raise ValueError("CI_TEST_ONLY")
        archive=anchor.checked_file(handoff.directory()/handoff.ZIP_NAME,
                                    handoff.ZIP_MAX)
        rc=receipt.inspect(archive,_template(Path(args.receipt_template),
                                             "ai-worker-external-receipt-template.json"))
        if rc["status"]!="EXTERNAL_RECEIPT_NOT_SUPPLIED":
            raise ValueError("EXTERNAL_RECEIPT_MUST_BE_UNCONFIGURED")
        ic=identity.inspect(_template(Path(args.identity_template),
                               "ai-worker-custodian-identity-template.json"),rc)
        if ic["status"]!="EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED":
            raise ValueError("IDENTITY_MUST_BE_UNCONFIGURED")
        lc=lifecycle.inspect(_template(Path(args.lifecycle_template),
                              "ai-worker-trust-key-lifecycle-template.json"),ic)
        if lc["status"]!="EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED":
            raise ValueError("LIFECYCLE_MUST_BE_UNCONFIGURED")
        result=inspect(_template(Path(args.temporal_template),
                      "ai-worker-trust-temporal-template.json"),lc)
        if result["status"]!="TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED":
            raise ValueError("TEMPORAL_AUTHORITY_NOT_ALLOWED")
        custody._write_denial_only(Path(args.report_json).absolute(),result)
        print("PASS: real synthetic Worker restart has no trusted clock, key-expiry or revocation-time authority")
    except (OSError,ValueError,TypeError,KeyError):
        print("WORKER_TRUST_TEMPORAL_DENIED",file=sys.stderr)
        raise SystemExit(17) from None

if __name__=="__main__":
    main()
