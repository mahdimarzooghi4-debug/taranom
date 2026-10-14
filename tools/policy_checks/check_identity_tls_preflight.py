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
import os
from pathlib import Path
import stat
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



def _inside_public(path: Path, repo: Path) -> bool:
    return (path.absolute().is_relative_to(repo.absolute())
            or path.resolve().is_relative_to(repo.resolve()))


def _no_symlink_parent(path: Path) -> bool:
    # Trusted local POSIX operator environment; this check and descriptor
    # verification reduce swaps, not a guarantee against privileged mounts.
    return not any(p.is_symlink() for p in path.parents)


def _read_private_target(path: Path, repo: Path) -> str:
    """Read ONLY a local owner-private one-line hostname; never command argv.

    A target file is an operator instruction, not independent evidence of
    authority, provider contract, DNS ownership or permission to use an IdP.
    """
    path = path.absolute()
    if (_inside_public(path, repo) or path.is_symlink()
            or not _no_symlink_parent(path)):
        raise ProbeError("PRIVATE_TARGET_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
    fd = None
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
        before = os.fstat(fd)
        if (not stat.S_ISREG(before.st_mode) or before.st_nlink != 1
                or before.st_size < 1 or before.st_size > 254
                or before.st_uid != os.getuid()
                or stat.S_IMODE(before.st_mode) & 0o077):
            raise ProbeError("UNSAFE_PRIVATE_TARGET_FILE")
        with os.fdopen(os.dup(fd), "rb") as stream:
            data = stream.read(255)
        after = os.fstat(fd)
        named = os.stat(path, follow_symlinks=False)
        def identity(s):
            return (s.st_dev, s.st_ino, s.st_size,
                    s.st_mtime_ns, s.st_ctime_ns)
        if (identity(before) != identity(after)
                or (named.st_dev, named.st_ino) != (after.st_dev, after.st_ino)
                or len(data) > 254):
            raise ProbeError("PRIVATE_TARGET_CHANGED_DURING_READ")
        # Permit exactly one trailing LF to support a private hostname file.
        value = data.removesuffix(b"\n")
        if not value or b"\n" in value or b"\r" in value:
            raise ProbeError("INVALID_PRIVATE_TARGET_CONTENT")
        try:
            host = value.decode("ascii")
        except UnicodeError as error:
            raise ProbeError("INVALID_PRIVATE_TARGET_CONTENT") from error
        return _validate_host(host)
    except ProbeError:
        raise
    except (OSError, TypeError, ValueError) as error:
        raise ProbeError("PRIVATE_TARGET_UNVERIFIABLE") from error
    finally:
        if fd is not None:
            os.close(fd)


def _write_private_report(path: Path, repo: Path, report: dict) -> None:
    """Never overwrite a preexisting file or write into the public checkout."""
    path = path.absolute()
    if (_inside_public(path, repo) or path.is_symlink()
            or not _no_symlink_parent(path)):
        raise ProbeError("PRIVATE_REPORT_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
    fd = None
    try:
        # Atomic exclusive create: defeats malicious pre-created symlink and
        # prevents overwriting preexisting private evidence or reports.
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            fd = None
            stream.write(json.dumps(report, sort_keys=True, indent=2) + "\n")
    except OSError as error:
        raise ProbeError("PRIVATE_REPORT_UNWRITABLE_OR_ALREADY_EXISTS") from error
    finally:
        if fd is not None:
            os.close(fd)

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--purpose", choices=PURPOSES, required=True)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2],
                        help="the public repository root for private ingress checks")
    parser.add_argument("--target-file", type=Path,
                        help="owner-private 0600 single-hostname file outside public Git; never argv host")
    # Suppress the legacy option from help and never echo its supplied value.
    parser.add_argument("--host", help=argparse.SUPPRESS)
    parser.add_argument("--report-json", required=True)
    args = parser.parse_args(argv)
    if args.host is not None:
        print("DENIED: PLAINTEXT_COMMAND_LINE_HOST_FORBIDDEN", file=sys.stderr)
        return 2
    repo = args.root.resolve()
    report_path = Path(args.report_json)
    if args.target_file is not None:
        try:
            # Validate BOTH private storage boundaries before any DNS/network.
            if (_inside_public(report_path.absolute(), repo)
                    or report_path.is_symlink()
                    or not _no_symlink_parent(report_path.absolute())):
                raise ProbeError("PRIVATE_REPORT_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
            if report_path.exists():
                raise ProbeError("PRIVATE_REPORT_UNWRITABLE_OR_ALREADY_EXISTS")
            name = _read_private_target(args.target_file, repo)
        except ProbeError as error:
            print("DENIED: " + error.code, file=sys.stderr)
            return 1
        result = inspect(args.purpose, name)
        try:
            _write_private_report(report_path, repo, result)
        except ProbeError as error:
            print("DENIED: " + error.code, file=sys.stderr)
            return 1
    else:
        # Public CI still cannot call any external identity service by default.
        result = inspect(args.purpose)
        # Public CI reports are not private evidence, but must not clobber
        # source files, existing reports or symlink targets.
        report_path.parent.mkdir(parents=True, exist_ok=True)
        try:
            with os.fdopen(os.open(
                report_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                0o600,
            ), "w", encoding="utf-8") as stream:
                stream.write(json.dumps(result, sort_keys=True, indent=2) + "\n")
        except OSError:
            print("DENIED: REPORT_UNWRITABLE_OR_ALREADY_EXISTS", file=sys.stderr)
            return 1
    print(f"Identity TLS preflight: {result['status']}; "
          "no authentication/proofing/legal approval; release_allowed=false.")
    if result["errors"]:
        print("DENIED: " + ",".join(result["errors"]), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
