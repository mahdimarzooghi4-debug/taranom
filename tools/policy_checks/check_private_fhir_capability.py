#!/usr/bin/env python3
"""TRN-TECH-085: opt-in read-only FHIR CapabilityStatement metadata preflight.

This never exchanges patient resources, bearer tokens, user credentials or
actual health data. Advertised metadata is untrusted, not verified coverage,
legal approval, provider contract, authorized interoperability or release.
GitHub Actions tests are OFFLINE and no selected host is stored in Public Git.
"""
from __future__ import annotations

import argparse
import http.client
import ipaddress
import json
import os
from pathlib import Path
import re
import socket
import ssl
import sys
import time

import build_private_evidence_batch as private
import check_identity_tls_preflight as transport

REQUEST_SCOPE = "PRIVATE_AUTHORIZED_FHIR_CAPABILITY_TARGET"
REPORT_SCOPE = "PRIVATE_FHIR_CAPABILITY_ADVERTISEMENT_ONLY"
MAX_BODY = 256 * 1024
MAX_HEADERS = 32 * 1024
MAX_RESOURCE_TYPES = 150
KNOWN_RESOURCE_TYPES = frozenset((
    "Patient", "Observation", "Condition", "Encounter", "Procedure",
    "Practitioner", "Organization", "Consent", "Coverage", "Claim",
    "ExplanationOfBenefit", "CarePlan", "MedicationRequest",
    "DiagnosticReport", "AllergyIntolerance", "Appointment",
))
RESOURCE_NAME = re.compile(r"[A-Z][A-Za-z0-9]{0,39}\Z")
BASE_PATH = re.compile(r"(?:/[A-Za-z0-9_-]{1,40}){0,5}\Z")
VERSION = re.compile(r"[1-9][0-9]?\.[0-9]{1,2}\.[0-9]{1,3}\Z")
INTERACTION = frozenset(("read", "vread", "update", "patch", "delete", "history-instance",
                         "history-type", "create", "search-type"))
FHIR_PATH_SUFFIX = "/metadata"


class FhirPreflightError(Exception):
    pass


def _deny(code):
    raise FhirPreflightError(code)


def _target(value):
    if (not isinstance(value, dict) or set(value) != {
            "schema_version", "scope", "host", "base_path"}
            or type(value["schema_version"]) is not int
            or value["schema_version"] != 1 or value["scope"] != REQUEST_SCOPE):
        _deny("INVALID_PRIVATE_FHIR_TARGET_SCHEMA")
    host = transport._validate_host(value["host"])
    base_path = value["base_path"]
    if (not isinstance(base_path, str) or len(base_path) > 160
            or not BASE_PATH.fullmatch(base_path)
            or "//" in base_path or "/../" in base_path):
        _deny("INVALID_PRIVATE_FHIR_BASE_PATH")
    return host, base_path + FHIR_PATH_SUFFIX


def _pairs(items):
    result = {}
    for key, value in items:
        if key in result:
            _deny("FHIR_DUPLICATE_JSON_KEY")
        result[key] = value
    return result


def _nonfinite(_):
    _deny("FHIR_NONFINITE_JSON_VALUE")


def _metadata(body):
    if not isinstance(body, bytes) or not 0 < len(body) <= MAX_BODY:
        _deny("FHIR_RESPONSE_SIZE_UNVERIFIABLE")
    try:
        obj = json.loads(body.decode("utf-8"), object_pairs_hook=_pairs,
                         parse_constant=_nonfinite)
    except (ValueError, UnicodeError, TypeError, RecursionError):
        _deny("FHIR_METADATA_JSON_UNVERIFIABLE")
    if (not isinstance(obj, dict) or len(obj) > 120
            or obj.get("resourceType") != "CapabilityStatement"):
        _deny("FHIR_CAPABILITY_RESOURCE_REQUIRED")
    version = obj.get("fhirVersion")
    if not isinstance(version, str) or not VERSION.fullmatch(version):
        _deny("FHIR_ADVERTISED_VERSION_UNVERIFIABLE")
    if obj.get("status") not in ("draft", "active", "retired", "unknown"):
        _deny("FHIR_CAPABILITY_STATUS_UNVERIFIABLE")
    if obj.get("kind") not in ("instance", "capability", "requirements"):
        _deny("FHIR_CAPABILITY_KIND_UNVERIFIABLE")
    rest = obj.get("rest")
    if not isinstance(rest, list) or not 0 < len(rest) <= 8:
        _deny("FHIR_SERVER_REST_CAPABILITIES_REQUIRED")
    advertised = {}
    saw_server = False
    for group in rest:
        if not isinstance(group, dict) or group.get("mode") not in ("client", "server"):
            _deny("FHIR_REST_MODE_UNVERIFIABLE")
        if group["mode"] != "server":
            continue
        saw_server = True
        resources = group.get("resource")
        if not isinstance(resources, list) or not 0 < len(resources) <= MAX_RESOURCE_TYPES:
            _deny("FHIR_SERVER_RESOURCES_UNVERIFIABLE")
        for resource in resources:
            if not isinstance(resource, dict):
                _deny("FHIR_RESOURCE_ADVERTISEMENT_UNVERIFIABLE")
            name = resource.get("type")
            if not isinstance(name, str) or not RESOURCE_NAME.fullmatch(name):
                _deny("FHIR_RESOURCE_ADVERTISEMENT_UNVERIFIABLE")
            interactions = resource.get("interaction", [])
            if (not isinstance(interactions, list) or len(interactions) > 20
                    or any(not isinstance(v, dict)
                           or v.get("code") not in INTERACTION for v in interactions)):
                _deny("FHIR_INTERACTIONS_UNVERIFIABLE")
            if name in KNOWN_RESOURCE_TYPES:
                advertised.setdefault(name, set()).update(i["code"] for i in interactions)
    if not saw_server:
        _deny("FHIR_SERVER_REST_CAPABILITIES_REQUIRED")
    # Only standard FHIR type/interaction names, never server URLs, names,
    # patient records, service claims or opaque vendor metadata.
    return {
        "observed_version_advertisement_only": version,
        "known_resource_types_advertised_only": sorted(advertised),
        "advertised_interactions_only": {
            k: sorted(v) for k, v in sorted(advertised.items())
        },
    }


def _read_fixed_metadata(host, ip, path):
    """One numeric-public-IP pinned CA/SNI TLS GET; never follow any URL."""
    try:
        context = ssl.create_default_context(purpose=ssl.Purpose.SERVER_AUTH)
        context.check_hostname = True
        context.verify_mode = ssl.CERT_REQUIRED
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        with socket.create_connection((ip, 443), timeout=5.0) as raw:
            with context.wrap_socket(raw, server_hostname=host) as conn:
                conn.settimeout(8.0)
                peer = ipaddress.ip_address(conn.getpeername()[0]).compressed
                if peer != ipaddress.ip_address(ip).compressed:
                    _deny("FHIR_CONNECTED_PEER_MISMATCH")
                if conn.version() not in ("TLSv1.2", "TLSv1.3"):
                    _deny("FHIR_TLS_VERSION_UNVERIFIABLE")
                certificate = conn.getpeercert()
                if not isinstance(certificate, dict) or not certificate.get("notAfter"):
                    _deny("FHIR_TLS_CERTIFICATE_UNVERIFIABLE")
                if ssl.cert_time_to_seconds(certificate["notAfter"]) <= time.time():
                    _deny("FHIR_TLS_CERTIFICATE_EXPIRED")
                conn.sendall((
                    "GET " + path + " HTTP/1.1\r\nHost: " + host +
                    "\r\nAccept: application/fhir+json\r\nAccept-Encoding: identity"
                    "\r\nCache-Control: no-store\r\nConnection: close"
                    "\r\nUser-Agent: Taranom-Private-FHIR-Preflight/1\r\n\r\n"
                ).encode("ascii"))
                response = http.client.HTTPResponse(conn, method="GET")
                response.begin()
                if sum(len(k) + len(v) + 4 for k, v in response.getheaders()) > MAX_HEADERS:
                    _deny("FHIR_HTTP_HEADERS_TOO_LARGE")
                if response.status != 200:
                    _deny("FHIR_HTTP_NOT_OK_OR_REDIRECT")
                media = response.getheader("Content-Type", "").split(";")[0].strip().lower()
                if media not in ("application/fhir+json", "application/json"):
                    _deny("FHIR_UNEXPECTED_CONTENT_TYPE")
                if response.getheader("Content-Encoding", "identity").lower() != "identity":
                    _deny("FHIR_COMPRESSED_RESPONSE_DENIED")
                length = response.getheader("Content-Length")
                if length is not None and (
                    not length.isdecimal() or int(length) > MAX_BODY
                ):
                    _deny("FHIR_RESPONSE_SIZE_UNVERIFIABLE")
                data = response.read(MAX_BODY + 1)
                if not data or len(data) > MAX_BODY:
                    _deny("FHIR_RESPONSE_SIZE_UNVERIFIABLE")
                return data
    except FhirPreflightError:
        raise
    except (OSError, ssl.SSLError, ValueError, TimeoutError,
            http.client.HTTPException, TypeError):
        _deny("FHIR_TRANSPORT_UNVERIFIABLE")


def inspect(target=None, *, fetcher=None):
    result = {
        "schema_version": 1, "scope": REPORT_SCOPE,
        "status": "TARGET_NOT_CONFIGURED" if target is None else "BLOCKED_UNVERIFIED",
        "capability_advertisement_observed": False,
        "fhir_data_exchange_verified": False,
        "remote_resource_read_performed": False,
        "provider_legal_identity_verified": False,
        "partner_contract_verified": False,
        "clinical_permission_verified": False,
        "member_consent_or_identity_verified": False,
        "insurance_rights_verified": False,
        "interoperability_admitted": False,
        "national_release_allowed": False,
        "release_allowed": False,
        "observed_version_advertisement_only": None,
        "known_resource_types_advertised_only": [],
        "advertised_interactions_only": {},
        "errors": []
    }
    if target is None:
        return result
    try:
        host, path = _target(target)
        addresses = transport._safe_addresses(host)
        metadata = _metadata((fetcher or _read_fixed_metadata)(host, addresses[0], path))
        result.update(metadata)
        result["status"] = "FHIR_CAPABILITY_SYNTAX_OBSERVED_NOT_AUTHORIZED"
        result["capability_advertisement_observed"] = True
    except transport.ProbeError as error:
        result["errors"] = [error.code]
    except FhirPreflightError as error:
        result["errors"] = [str(error)]
    except Exception:
        result["errors"] = ["FHIR_CAPABILITY_UNVERIFIABLE"]
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[2])
    parser.add_argument("--target-file", type=Path)
    parser.add_argument("--acknowledge-authorized-technical-probe",
                        action="store_true")
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args(argv)
    public_root = args.root.resolve()
    try:
        if args.target_file is None:
            # The default CI path NEVER performs external DNS/HTTP or selects a provider.
            if args.acknowledge_authorized_technical_probe:
                _deny("NO_PRIVATE_TARGET_CONFIGURED")
            result = inspect()
            args.report_json.parent.mkdir(parents=True, exist_ok=True)
            args.report_json.write_text(
                json.dumps(result, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        else:
            if not args.acknowledge_authorized_technical_probe:
                _deny("EXPLICIT_AUTHORIZED_TECHNICAL_PROBE_REQUIRED")
            target_file = private._private_path(args.target_file, public_root,
                                                output=False)
            report_file = private._private_path(args.report_json, public_root,
                                                output=True)
            if target_file == report_file:
                _deny("PRIVATE_INPUT_OUTPUT_SEPARATION_REQUIRED")
            target = private._read_request(target_file, public_root)
            # A user-supplied scope/host is NOT legal approval; authorization
            # must occur outside the tool before this explicit operator action.
            _target(target)
            result = inspect(target)
            ident = private._write_new(report_file, result)
            if private._read_request(report_file, public_root) != result:
                private._unlink_ours(report_file, ident)
                _deny("PRIVATE_FHIR_REPORT_INTEGRITY_UNVERIFIABLE")
    except (private.BatchError, transport.ProbeError, FhirPreflightError) as error:
        print("DENIED: " + str(error), file=sys.stderr)
        return 1
    except Exception:
        print("DENIED: FHIR_PREFLIGHT_UNVERIFIABLE", file=sys.stderr)
        return 1
    print("Private FHIR preflight: " + result["status"] +
          "; data_exchange_verified=false; release_allowed=false.")
    if result["errors"]:
        print("DENIED: " + ",".join(result["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
