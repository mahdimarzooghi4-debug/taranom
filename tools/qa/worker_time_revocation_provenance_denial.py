#!/usr/bin/env python3
"""TRN-TECH-163: never authenticate self-asserted time/revocation origins.

The synthetic digest is NOT an external signature. Neither an issuer, trusted
clock, approved public key, revocation authority nor external service exists.
This module is test-only, offline, fixed-output denial; no wall clock or I/O
except reading empty repository templates and synthetic handoff ZIP in CI.
"""
from __future__ import annotations

import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import sys

import check_worker_external_custody as custody
import worker_evidence_anchor as anchor
import worker_evidence_handoff as handoff
import worker_external_receipt_denial as receipt
import worker_custodian_identity_boundary as identity
import worker_trust_key_lifecycle_denial as lifecycle
import worker_trust_temporal_denial as temporal

SCHEMA=1
SCOPE="TRN_TECH_163_UNTRUSTED_TIME_REVOCATION_SOURCES"
MAX_BYTES=12000
MAX_NOTICES=6  # parser fixture limit; NOT operational policy/threshold
TOP={"schema_version","scope","candidate"}
CANDIDATE={"transfer_ref","challenge_ref","clock_claim","revocation_notices"}
CLOCK={"source_ref","assertion_ref","claimed_utc","content_sha256"}
NOTICE={"source_ref","assertion_ref","key_ref","kind",
        "claimed_effective_at","claimed_observed_at","content_sha256"}
HEX64=__import__("re").compile(r"[0-9A-F]{64}\Z")
PREVIOUS={"TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED",
          "TEMPORAL_CLAIMS_REQUIRE_INDEPENDENT_REVIEW"}
FLAGS=(*temporal.FLAGS,"clock_source_authenticated",
       "revocation_source_authenticated","attestation_signature_verified",
       "revocation_notice_verified","source_separation_verified",
       "external_revocation_authority_verified")

def denied(status,checked=False):
    output={"schema_version":SCHEMA,"scope":SCOPE,"status":status,
            "synthetic_archive_shape_checked":checked}
    output.update({k:False for k in FLAGS})
    return output

def prior_valid(ctx):
    return (type(ctx) is dict and set(ctx)==set(temporal.denied("unused")) and
            type(ctx.get("schema_version")) is int and
            ctx.get("schema_version")==temporal.SCHEMA and
            ctx.get("scope")==temporal.SCOPE and
            type(ctx.get("status")) is str and ctx["status"] in PREVIOUS and
            ctx.get("synthetic_archive_shape_checked") is True and
            all(ctx.get(k) is False for k in temporal.FLAGS))

def untrusted_digest(body,transfer,challenge):
    """Public canonical SHA: integrity comparison only, NOT authenticity."""
    material=anchor.canonical({"transfer_ref":transfer,
                               "challenge_ref":challenge,"claim":body})
    return hashlib.sha256(material).hexdigest().upper()

def exact_claim(value,fields):
    return (type(value) is dict and set(value)==fields and
            all(type(value[k]) is str and
                lifecycle.TEST_REF.fullmatch(value[k]) is not None
                for k in fields-{"claimed_utc","claimed_effective_at",
                                 "claimed_observed_at","content_sha256","kind"}) and
            type(value["content_sha256"]) is str and
            HEX64.fullmatch(value["content_sha256"]) is not None)

def inspect(raw,context,*,
            expected_transfer=identity.TEST_TRANSFER,
            expected_challenge=identity.TEST_CHALLENGE):
    if not prior_valid(context):
        return denied("DENIED_UNTRUSTED_TEMPORAL_CONTEXT")
    result=denied("DENIED_INVALID_SOURCE_CLAIMS",True)
    try:
        if type(raw) is not bytes or not 0<len(raw)<=MAX_BYTES:
            return result
        root=json.loads(raw.decode("utf-8"),object_pairs_hook=custody.unique,
                        parse_constant=custody.reject_constant)
        if (type(root) is not dict or set(root)!=TOP or
            type(root["schema_version"]) is not int or
            root["schema_version"]!=SCHEMA or root["scope"]!=SCOPE):
            return result
        candidate=root["candidate"]
        if candidate is None:
            result["status"]="TIME_REVOCATION_SOURCES_NOT_CONFIGURED"
            return result
        if (type(candidate) is not dict or set(candidate)!=CANDIDATE or
            any(type(candidate[k]) is not str or
                lifecycle.TEST_REF.fullmatch(candidate[k]) is None
                for k in ("transfer_ref","challenge_ref")) or
            type(candidate["revocation_notices"]) is not list or
            not 1<=len(candidate["revocation_notices"])<=MAX_NOTICES):
            return result
        transfer=candidate["transfer_ref"]
        challenge=candidate["challenge_ref"]
        if transfer!=expected_transfer or challenge!=expected_challenge:
            result["status"]="DENIED_SOURCE_TRANSFER_OR_CHALLENGE_REPLAY"
            return result
        clock=candidate["clock_claim"]
        if not exact_claim(clock,CLOCK):
            return result
        clock_time=temporal.instant(clock["claimed_utc"])
        clock_body={k:clock[k] for k in CLOCK-{"content_sha256"}}
        if not hmac.compare_digest(clock["content_sha256"],
                                   untrusted_digest(clock_body,transfer,challenge)):
            result["status"]="DENIED_TIME_CLAIM_DIGEST_MISMATCH"
            return result

        used_refs={clock["assertion_ref"]}
        used_keys=set()
        last_observed=None
        for notice in candidate["revocation_notices"]:
            if (not exact_claim(notice,NOTICE) or
                type(notice["kind"]) is not str or
                notice["kind"] not in ("REVOKED","RETIRED")):
                return result
            event_id=notice["assertion_ref"]
            if event_id in used_refs:
                result["status"]="DENIED_REPLAYED_SOURCE_ASSERTION"
                return result
            used_refs.add(event_id)
            if notice["key_ref"] in used_keys:
                result["status"]="DENIED_CONFLICTING_KEY_TERMINATION_CLAIMS"
                return result
            used_keys.add(notice["key_ref"])
            effective=temporal.instant(notice["claimed_effective_at"])
            observed=temporal.instant(notice["claimed_observed_at"])
            if effective>observed:
                result["status"]="DENIED_TERMINATION_EFFECTIVE_AFTER_OBSERVED"
                return result
            if last_observed is not None and observed<=last_observed:
                result["status"]="DENIED_SOURCE_NOTICE_OBSERVATION_ORDER"
                return result
            if observed>clock_time:
                result["status"]="DENIED_CLOCK_NOTICE_TIME_CONTRADICTION"
                return result
            last_observed=observed
            body={k:notice[k] for k in NOTICE-{"content_sha256"}}
            if not hmac.compare_digest(notice["content_sha256"],
                                       untrusted_digest(body,transfer,challenge)):
                result["status"]="DENIED_REVOCATION_NOTICE_DIGEST_MISMATCH"
                return result
        # Digests only show that CLAIMANT can hash its own assertions.
        # They do not authenticate an origin or verify a real revocation.
        result["status"]="SOURCE_CLAIMS_REQUIRE_INDEPENDENT_PROVENANCE_REVIEW"
        return result
    except (ValueError,TypeError,KeyError,UnicodeError,OverflowError,
            custody.InvalidCustodyContract,json.JSONDecodeError):
        return result

def _template(path,name):
    expected=Path(__file__).resolve().parents[2]/"docs/governance"/name
    if path.is_symlink() or path.absolute()!=expected:
        raise ValueError("UNSUPPORTED_TEMPLATE")
    return custody._secure_input(path)

def main():
    cli=argparse.ArgumentParser(description="CI-only denial of claimed time/revocation authority")
    for flag in ("source","temporal","lifecycle","identity","receipt"):
        cli.add_argument("--"+flag+"-template",required=True)
    cli.add_argument("--report-json",required=True)
    args=cli.parse_args()
    try:
        if not all(os.environ.get(k)=="CI_EPHEMERAL_ONLY" for k in
                   ("TARANOM_CENTER_DEMO_TEST_ONLY",
                    "TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")):
            raise ValueError("TEST_ONLY")
        packed=anchor.checked_file(handoff.directory()/handoff.ZIP_NAME,
                                   handoff.ZIP_MAX)
        rc=receipt.inspect(packed,_template(Path(args.receipt_template),
                                            "ai-worker-external-receipt-template.json"))
        if rc["status"]!="EXTERNAL_RECEIPT_NOT_SUPPLIED":
            raise ValueError("NO_REAL_RECEIPT")
        ic=identity.inspect(_template(Path(args.identity_template),
                      "ai-worker-custodian-identity-template.json"),rc)
        if ic["status"]!="EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED":
            raise ValueError("NO_REAL_IDENTITY")
        lc=lifecycle.inspect(_template(Path(args.lifecycle_template),
                         "ai-worker-trust-key-lifecycle-template.json"),ic)
        if lc["status"]!="EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED":
            raise ValueError("NO_REAL_LIFECYCLE")
        tc=temporal.inspect(_template(Path(args.temporal_template),
                          "ai-worker-trust-temporal-template.json"),lc)
        if tc["status"]!="TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED":
            raise ValueError("NO_REAL_TEMPORAL_SOURCE")
        out=inspect(_template(Path(args.source_template),
                       "ai-worker-time-revocation-source-template.json"),tc)
        if out["status"]!="TIME_REVOCATION_SOURCES_NOT_CONFIGURED":
            raise ValueError("NO_REAL_SOURCE")
        custody._write_denial_only(Path(args.report_json).absolute(),out)
        print("PASS: real synthetic Worker restart has no authenticated clock or revocation source")
    except (OSError,ValueError,TypeError,KeyError):
        print("WORKER_TIME_REVOCATION_SOURCE_DENIED",file=sys.stderr)
        raise SystemExit(17) from None

if __name__=="__main__":
    main()
