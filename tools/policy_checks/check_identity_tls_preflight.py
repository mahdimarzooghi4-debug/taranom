#!/usr/bin/env python3
"""TRN-TECH-067: opt-in, read-only TLS transport check for a future identity host.

NO authentication, identity verification, provider selection, legal approval,
HTTP exchange, personal data, credentials, enrollment, membership activation
or Production release can result from this probe.
"""
from __future__ import annotations

import argparse
import ipaddress
import json
import re
import socket
import ssl
import sys
import time

PURPOSES = ("AUTHENTICATION_IDP", "IDENTITY_PROOFING")
LABEL = re.compile(r"[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\Z")
DENIED_SUFFIXES = (
    "localhost", "local", "internal", "invalid", "example",
    "test", "onion", "home.arpa",
)
PORT = 443
TIMEOUT_SECONDS = 5.0
MAX_ADDRESSES = 32


class ProbeError(Exception):
    def __init__(self, code: str):
        self.code = code
        super().__init__(code)


def _validate_host(host: str) -> str:
    if not isinstance(host, str) or not host or len(host) > 253:
        raise ProbeError("INVALID_HOST")
    if host != host.strip() or not host.isascii() or host.endswith("."):
        raise ProbeError("INVALID_HOST")
    name = host.lower()
    if any(x in name for x in (":", "/", "@", "?", "#", "\\", "%")):
        raise ProbeError("INVALID_HOST")
    try:
        ipaddress.ip_address(name)
    except ValueError:
        pass
    else:
        raise ProbeError("IP_LITERAL_NOT_ALLOWED")
    parts = name.split(".")
    if len(parts) < 2 or not all(LABEL.fullmatch(part) for part in parts):
        raise ProbeError("INVALID_HOST")
    if any(name == suffix or name.endswith("." + suffix)
           for suffix in DENIED_SUFFIXES):
        raise ProbeError("NONPUBLIC_HOST_SUFFIX")
    if parts[-1].isdigit():
        raise ProbeError("INVALID_HOST")
    return name


def _safe_addresses(name: str) -> tuple[str, ...]:
    try:
        records = socket.getaddrinfo(name, PORT, type=socket.SOCK_STREAM,
                                     proto=socket.IPPROTO_TCP)
    except (OSError, ValueError, UnicodeError) as error:
        raise ProbeError("DNS_UNAVAILABLE") from error
    if not records or len(records) > MAX_ADDRESSES:
        raise ProbeError("DNS_UNAVAILABLE")
    addresses = []
    for family, kind, protocol, _, sockaddr in records:
        if family not in (socket.AF_INET, socket.AF_INET6) or (
                kind != socket.SOCK_STREAM or protocol not in (0, socket.IPPROTO_TCP)):
            raise ProbeError("UNEXPECTED_DNS_RESULT")
        try:
            address = ipaddress.ip_address(sockaddr[0])
        except (ValueError, IndexError, TypeError) as error:
            raise ProbeError("UNEXPECTED_DNS_RESULT") from error
        # Fail closed across the *entire* DNS answer set. Never select a
        # public answer while silently ignoring a private/loopback result.
        if not address.is_global:
            raise ProbeError("NONPUBLIC_DNS_DESTINATION")
        if address.compressed not in addresses:
            addresses.append(address.compressed)
    return tuple(addresses)


def _check_tls(name: str, address: str) -> None:
    """Connect to the approved numeric DNS answer, never a second DNS lookup.

    TLS SNI and hostname verification still use the original hostname.
    No HTTP request follows the handshake and no peer identity is logged.
    """
    try:
        ctx = ssl.create_default_context(purpose=ssl.Purpose.SERVER_AUTH)
        ctx.verify_mode = ssl.CERT_REQUIRED
        ctx.check_hostname = True
        ctx.minimum_version = ssl.TLSVersion.TLSv1_2
        with socket.create_connection((address, PORT), timeout=TIMEOUT_SECONDS) as raw:
            with ctx.wrap_socket(raw, server_hostname=name) as secured:
                peer = ipaddress.ip_address(secured.getpeername()[0])
                if peer.compressed != address:
                    raise ProbeError("CONNECTED_PEER_MISMATCH")
                if secured.version() not in ("TLSv1.2", "TLSv1.3"):
                    raise ProbeError("UNACCEPTABLE_TLS_VERSION")
                cert = secured.getpeercert()
                if not isinstance(cert, dict) or not cert.get("notAfter"):
                    raise ProbeError("PEER_CERTIFICATE_UNVERIFIABLE")
                if ssl.cert_time_to_seconds(cert["notAfter"]) <= time.time():
                    raise ProbeError("PEER_CERTIFICATE_EXPIRED")
    except ProbeError:
        raise
    except (OSError, ssl.SSLError, ValueError, TypeError) as error:
        # Deliberately redact host/IP/certificate/SSL exception text.
        raise ProbeError("TLS_TRANSPORT_UNVERIFIABLE") from error


def inspect(purpose: str, host: str | None = None) -> dict:
    report = {
        "schema_version": 1,
        "scope": "OPERATOR_SELECTED_EXTERNAL_IDENTITY_TLS_TRANSPORT",
        "purpose": purpose if purpose in PURPOSES else "INVALID",
        "status": "BLOCKED_UNVERIFIED",
        "tls_transport_reachable": False,
        "identity_proofing_verified": False,
        "member_authentication_verified": False,
        "external_provider_contract_verified": False,
        "member_activation_allowed": False,
        "national_release_allowed": False,
        "errors": [],
    }
    try:
        if purpose not in PURPOSES:
            raise ProbeError("UNRECOGNIZED_IDENTITY_PURPOSE")
        if host is None:
            # Explicit non-network CI/default path. Never a fake positive.
            report["status"] = "TARGET_NOT_CONFIGURED"
            return report
        name = _validate_host(host)
        answers = _safe_addresses(name)
        # Try the first globally routable numeric answer only. A failure is
        # diagnostic, not a reason to try alternative trust roots or downgrade.
        _check_tls(name, answers[0])
        report["status"] = "TLS_TRANSPORT_REACHABLE_ONLY"
        report["tls_transport_reachable"] = True
    except ProbeError as error:
        report["errors"] = [error.code]
    except Exception:
        # No raw exception path/host/certificate content crosses the boundary.
        report["errors"] = ["PROBE_UNVERIFIABLE"]
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--purpose", choices=PURPOSES, required=True)
    parser.add_argument("--host",
                        help="operator-approved public DNS hostname only; no URL/IP/credentials")
    parser.add_argument("--report-json", required=True)
    args = parser.parse_args(argv)
    result = inspect(args.purpose, args.host)
    from pathlib import Path
    path = Path(args.report_json)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(result, sort_keys=True, indent=2) + "\n",
                    encoding="utf-8")
    print(f"Identity TLS preflight: {result['status']}; "
          "no authentication/proofing/legal approval; release_allowed=false.")
    if result["errors"]:
        print("DENIED: " + ",".join(result["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
