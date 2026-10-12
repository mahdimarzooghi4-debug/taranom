#!/usr/bin/env python3
"""TRN-TECH-083: strictly opt-in, anonymous, read-only OIDC discovery preflight.

Provider chosen privately by an authorized operator. NO authentication,
accounts, IdP selection, JWT validation, contract approval, or release.
GitHub CI never supplies a target or uses the network.
"""
from __future__ import annotations

import argparse
import http.client
import json
import os
from pathlib import Path
import socket
import ssl
import sys
from urllib.parse import urlsplit

import check_identity_tls_preflight as transport
import build_private_evidence_batch as private
import check_private_external_evidence as custody

MAX_METADATA_BYTES = 65536
MAX_HEADERS_BYTES = 65536
WELL_KNOWN = "/.well-known/openid-configuration"
ALGORITHMS = frozenset(("RS256", "RS384", "RS512", "PS256", "PS384",
                        "PS512", "ES256", "ES384", "ES512"))


class DiscoveryError(Exception):
    pass


def _deny(code: str):
    raise DiscoveryError(code)


def _safe_https_endpoint(url, expected_host: str, *, issuer=False) -> bool:
    if not isinstance(url, str) or not url or len(url) > 2048 or not url.isascii():
        return False
    if any(ord(c) < 33 or ord(c) == 127 or c == "\\" for c in url):
        return False
    try:
        parts = urlsplit(url)
        return (parts.scheme == "https"
                and parts.hostname == expected_host
                and parts.port in (None, 443)
                and parts.username is None and parts.password is None
                and not parts.query and not parts.fragment
                and (parts.path in ("", "/") if issuer else
                     parts.path.startswith("/") and len(parts.path) > 1)
                and parts.netloc in (expected_host, expected_host + ":443"))
    except ValueError:
        return False


def _unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            _deny("DUPLICATE_DISCOVERY_JSON_KEY")
        result[key] = value
    return result


def _nonfinite(_):
    _deny("NONFINITE_DISCOVERY_VALUE")


def _validate_metadata(raw: bytes, host: str) -> dict:
    if not raw or len(raw) > MAX_METADATA_BYTES:
        _deny("DISCOVERY_SIZE_UNVERIFIABLE")
    try:
        meta = json.loads(raw.decode("utf-8"), object_pairs_hook=_unique,
                          parse_constant=_nonfinite)
    except (ValueError, UnicodeError, TypeError):
        _deny("DISCOVERY_JSON_UNVERIFIABLE")
    if not isinstance(meta, dict) or len(meta) > 120:
        _deny("DISCOVERY_SCHEMA_UNVERIFIABLE")
    if meta.get("issuer") != "https://" + host or not _safe_https_endpoint(
            meta.get("issuer"), host, issuer=True):
        _deny("ISSUER_BINDING_MISMATCH")
    for key in ("authorization_endpoint", "token_endpoint", "jwks_uri"):
        if not _safe_https_endpoint(meta.get(key), host):
            _deny("UNTRUSTED_DISCOVERY_ENDPOINT")
    for key in ("response_types_supported", "subject_types_supported",
                "id_token_signing_alg_values_supported"):
        v = meta.get(key)
        if (not isinstance(v, list) or not v or len(v) > 60
                or any(not isinstance(s, str) or len(s) > 100 for s in v)):
            _deny("DISCOVERY_CAPABILITY_SCHEMA_UNVERIFIABLE")
    if "code" not in meta["response_types_supported"]:
        _deny("AUTHORIZATION_CODE_FLOW_NOT_ADVERTISED")
    if not set(meta["subject_types_supported"]) <= {"public", "pairwise"}:
        _deny("SUBJECT_TYPE_UNVERIFIABLE")
    if not (set(meta["id_token_signing_alg_values_supported"]) & ALGORITHMS):
        _deny("APPROVED_ALGORITHM_NOT_ADVERTISED")
    if "none" in meta["id_token_signing_alg_values_supported"]:
        _deny("UNSIGNED_TOKENS_ADVERTISED")
    # Future browser login must require PKCE, but its implementation remains
    # blocked pending TRN-TECH-012 human contract approval.
    if "S256" not in meta.get("code_challenge_methods_supported", []):
        _deny("PKCE_S256_NOT_ADVERTISED")
    # Never imply tokens or keys were fetched/validated.
    return {
        "authorization_code_advertised": True,
        "pkce_s256_advertised": True,
        "asymmetric_id_token_alg_advertised": True,
        "jwks_url_syntax_checked_only": True,
    }


def _fetch_pinned(host: str, ip: str) -> bytes:
    """Exactly one anonymous GET through numeric-IP socket + verified SNI."""
    try:
        ctx = ssl.create_default_context(purpose=ssl.Purpose.SERVER_AUTH)
        ctx.check_hostname = True
        ctx.verify_mode = ssl.CERT_REQUIRED
        ctx.minimum_version = ssl.TLSVersion.TLSv1_2
        with socket.create_connection((ip, 443), timeout=5.0) as tcp:
            with ctx.wrap_socket(tcp, server_hostname=host) as secured:
                if secured.getpeername()[0] != ip:
                    _deny("CONNECTED_PEER_MISMATCH")
                if secured.version() not in ("TLSv1.2", "TLSv1.3"):
                    _deny("TLS_VERSION_UNVERIFIABLE")
                certificate = secured.getpeercert()
                if not isinstance(certificate, dict) or not certificate.get("notAfter"):
                    _deny("TLS_CERTIFICATE_UNVERIFIABLE")
                if ssl.cert_time_to_seconds(certificate["notAfter"]) <= __import__("time").time():
                    _deny("TLS_CERTIFICATE_EXPIRED")
                secured.sendall((
                    "GET " + WELL_KNOWN + " HTTP/1.1\r\nHost: " + host +
                    "\r\nAccept: application/json\r\nAccept-Encoding: identity"
                    "\r\nConnection: close\r\nUser-Agent: Taranom-ReadOnly-Preflight/1"
                    "\r\n\r\n").encode("ascii"))
                response = http.client.HTTPResponse(secured, method="GET")
                response.begin()
                if response.status != 200:
                    _deny("DISCOVERY_HTTP_NOT_OK_OR_REDIRECT")
                media = response.getheader("Content-Type", "").split(";")[0].strip().lower()
                if media != "application/json":
                    _deny("DISCOVERY_CONTENT_TYPE_UNVERIFIABLE")
                if response.getheader("Content-Encoding", "identity").lower() != "identity":
                    _deny("DISCOVERY_CONTENT_ENCODING_UNVERIFIABLE")
                declared = response.getheader("Content-Length")
                if declared is not None and (not declared.isdecimal()
                                             or int(declared) > MAX_METADATA_BYTES):
                    _deny("DISCOVERY_SIZE_UNVERIFIABLE")
                payload = response.read(MAX_METADATA_BYTES + 1)
                if not payload or len(payload) > MAX_METADATA_BYTES:
                    _deny("DISCOVERY_SIZE_UNVERIFIABLE")
                return payload
    except DiscoveryError:
        raise
    except (OSError, ValueError, ssl.SSLError, http.client.HTTPException,
            TimeoutError, TypeError):
        _deny("OIDC_TRANSPORT_UNVERIFIABLE")


def inspect(host: str | None = None, *, fetcher=None) -> dict:
    result = {
        "schema_version": 1, "scope": "PRIVATE_SELECTED_IDP_OIDC_DISCOVERY_ONLY",
        "status": "BLOCKED_UNVERIFIED",
        "read_only_metadata_observed": False,
        "endpoint_syntax_checked_only": False,
        "jwks_fetched_or_verified": False,
        "id_token_signature_verified": False,
        "browser_authentication_enabled": False,
        "member_authentication_verified": False,
        "identity_proofing_verified": False,
        "external_provider_contract_verified": False,
        "member_activation_allowed": False,
        "national_release_allowed": False,
        "release_allowed": False, "errors": []
    }
    if host is None:
        result["status"] = "TARGET_NOT_CONFIGURED"
        return result
    try:
        name = transport._validate_host(host)
        addresses = transport._safe_addresses(name)
        actual = (fetcher or _fetch_pinned)(name, addresses[0])
        _validate_metadata(actual, name)
        result["status"] = "DISCOVERY_SYNTAX_OBSERVED_NOT_AUTHORIZED"
        result["read_only_metadata_observed"] = True
        result["endpoint_syntax_checked_only"] = True
    except transport.ProbeError as exc:
        result["errors"] = [exc.code]
    except DiscoveryError as exc:
        result["errors"] = [str(exc)]
    except Exception:
        result["errors"] = ["DISCOVERY_UNVERIFIABLE"]
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path,
                        default=Path(__file__).resolve().parents[2])
    parser.add_argument("--target-file", type=Path)
    parser.add_argument("--report-json", type=Path, required=True)
    args = parser.parse_args(argv)
    root = args.root.resolve()
    try:
        if args.target_file is not None:
            # Both paths must pass confidential custody BEFORE network access.
            report = private._private_path(args.report_json, root, output=True)
            if report == args.target_file.absolute():
                _deny("PRIVATE_TARGET_REPORT_PATHS_MUST_DIFFER")
            host = transport._read_private_target(args.target_file, root)
            result = inspect(host)
            private._write_new(report, result)
        else:
            result = inspect()
            args.report_json.parent.mkdir(parents=True, exist_ok=True)
            args.report_json.write_text(json.dumps(result, sort_keys=True, indent=2)
                                        + "\n", encoding="utf-8")
    except (DiscoveryError, transport.ProbeError, private.BatchError) as exc:
        # Fixed enum codes only. Never print private hosts or response content.
        print("DENIED: " + str(exc), file=sys.stderr)
        return 1
    except Exception:
        print("DENIED: PRIVATE_DISCOVERY_IO_UNVERIFIABLE", file=sys.stderr)
        return 1
    print("Private OIDC discovery: " + result["status"] +
          "; member_authentication_verified=false; release_allowed=false.")
    if result["errors"]:
        print("DENIED: " + ",".join(result["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
