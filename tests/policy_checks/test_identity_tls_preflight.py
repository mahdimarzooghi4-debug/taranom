"""Offline tests only: no network, patient data, credentials or selected IdP."""
from __future__ import annotations

import json
import pathlib
import socket
import ssl
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "policy_checks"))
import check_identity_tls_preflight as preflight  # noqa: E402

PUBLIC = "identity.future-partner.com"
GOOD = "1.1.1.1"
SECOND = "8.8.8.8"
CERT = {"notAfter": "Dec 31 23:59:59 2099 GMT"}


class FakeStream:
    def __init__(self, peer=GOOD, tls_version="TLSv1.3", cert=None):
        self.peer, self.tls_version = peer, tls_version
        self.cert = CERT if cert is None else cert

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def getpeername(self):
        return (self.peer, 443)

    def version(self):
        return self.tls_version

    def getpeercert(self):
        return self.cert


class FakeContext:
    def __init__(self, stream=None):
        self.stream = stream or FakeStream()
        self.check_hostname = None
        self.verify_mode = None
        self.minimum_version = None

    def wrap_socket(self, raw, server_hostname):
        assert server_hostname == PUBLIC
        assert self.check_hostname is True
        assert self.verify_mode == ssl.CERT_REQUIRED
        assert self.minimum_version == ssl.TLSVersion.TLSv1_2
        return self.stream


def answers(*ips):
    return [(socket.AF_INET, socket.SOCK_STREAM, socket.IPPROTO_TCP, "",
             (ip, 443)) for ip in ips]


class IdentityTlsPreflightTests(unittest.TestCase):
    def denied(self, host, reason, purpose="AUTHENTICATION_IDP"):
        result = preflight.inspect(purpose, host)
        self.assertEqual(result["status"], "BLOCKED_UNVERIFIED", result)
        self.assertEqual(result["errors"], [reason], result)
        self.assertFalse(result["tls_transport_reachable"])
        self.assertFalse(result["member_authentication_verified"])
        self.assertFalse(result["identity_proofing_verified"])
        self.assertFalse(result["national_release_allowed"])
        self.assertNotIn(PUBLIC, json.dumps(result))
        return result

    def test_no_target_is_explicit_unconfigured_without_network(self):
        with patch.object(preflight.socket, "getaddrinfo", side_effect=AssertionError):
            result = preflight.inspect("AUTHENTICATION_IDP")
        self.assertEqual(result["status"], "TARGET_NOT_CONFIGURED")
        self.assertEqual(result["errors"], [])
        self.assertFalse(result["tls_transport_reachable"])

    def test_purposes_remain_independent(self):
        for purpose in preflight.PURPOSES:
            report = preflight.inspect(purpose)
            self.assertEqual(report["purpose"], purpose)
            self.assertFalse(report["member_activation_allowed"])
            self.assertFalse(report["external_provider_contract_verified"])

    def test_unsupported_purpose_is_denied(self):
        self.denied(None, "UNRECOGNIZED_IDENTITY_PURPOSE", "AUTO_MEMBERSHIP")

    def test_url_and_userinfo_rejected(self):
        for host in ("https://identity.future-partner.com",
                     "user:secret@identity.future-partner.com",
                     "identity.future-partner.com/oidc",
                     "identity.future-partner.com:443"):
            with self.subTest(host=host):
                self.denied(host, "INVALID_HOST")

    def test_ip_literals_rejected(self):
        self.denied(GOOD, "IP_LITERAL_NOT_ALLOWED")
        self.denied("127.0.0.1", "IP_LITERAL_NOT_ALLOWED")
        self.denied("::1", "INVALID_HOST")

    def test_local_reserved_suffixes_rejected(self):
        for name in ("login.local", "login.internal", "login.test",
                     "login.example", "login.invalid", "login.localhost",
                     "login.home.arpa", "login.onion"):
            with self.subTest(name=name):
                self.denied(name, "NONPUBLIC_HOST_SUFFIX")

    def test_unicode_whitespace_and_trailing_dot_rejected(self):
        for host in ("lögin.vendor.com", " login.vendor.com", "login.vendor.com ",
                     "login.vendor.com.", "login..vendor.com"):
            with self.subTest(host=host):
                self.denied(host, "INVALID_HOST")

    def test_oversized_name_and_bad_labels_rejected(self):
        for host in ("a" * 254 + ".com", "-login.vendor.com",
                     "login_.vendor.com", "login"):
            self.denied(host, "INVALID_HOST")

    def test_no_dns_result_is_denied(self):
        with patch.object(preflight.socket, "getaddrinfo", return_value=[]):
            self.denied(PUBLIC, "DNS_UNAVAILABLE")

    def test_dns_error_is_denied_without_raw_exception(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          side_effect=OSError("private target secret")):
            result = self.denied(PUBLIC, "DNS_UNAVAILABLE")
        self.assertNotIn("private target secret", json.dumps(result))

    def test_private_dns_answer_denied_even_with_public_answer(self):
        for ip in ("10.2.3.4", "127.0.0.1", "169.254.1.2",
                   "192.168.1.1", "100.64.0.10", "0.0.0.0"):
            with self.subTest(ip=ip):
                with patch.object(preflight.socket, "getaddrinfo",
                                  return_value=answers(GOOD, ip)):
                    self.denied(PUBLIC, "NONPUBLIC_DNS_DESTINATION")

    def test_ipv6_local_dns_denied(self):
        records = [(socket.AF_INET6, socket.SOCK_STREAM, socket.IPPROTO_TCP,
                    "", ("::1", 443, 0, 0))]
        with patch.object(preflight.socket, "getaddrinfo", return_value=records):
            self.denied(PUBLIC, "NONPUBLIC_DNS_DESTINATION")

    def test_unexpected_dns_record_denied(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=[(socket.AF_INET, socket.SOCK_DGRAM,
                                         17, "", (GOOD, 443))]):
            self.denied(PUBLIC, "UNEXPECTED_DNS_RESULT")

    def test_success_is_transport_only_and_numeric_ip_pinned(self):
        ctx = FakeContext()
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD, SECOND)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()) as connector, \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=ctx):
            result = preflight.inspect("AUTHENTICATION_IDP", PUBLIC)
        connector.assert_called_once_with((GOOD, 443), timeout=5.0)
        self.assertEqual(result["status"], "TLS_TRANSPORT_REACHABLE_ONLY")
        self.assertTrue(result["tls_transport_reachable"])
        self.assertFalse(result["member_authentication_verified"])
        self.assertFalse(result["identity_proofing_verified"])
        self.assertFalse(result["member_activation_allowed"])
        self.assertFalse(result["national_release_allowed"])
        self.assertNotIn(PUBLIC, json.dumps(result))
        self.assertNotIn(GOOD, json.dumps(result))

    def test_probe_of_proofing_endpoint_does_not_imply_login(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()), \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=FakeContext()):
            result = preflight.inspect("IDENTITY_PROOFING", PUBLIC)
        self.assertEqual(result["purpose"], "IDENTITY_PROOFING")
        self.assertFalse(result["member_authentication_verified"])

    def test_post_dns_peer_switch_is_rejected(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()), \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=FakeContext(FakeStream(peer=SECOND))):
            self.denied(PUBLIC, "CONNECTED_PEER_MISMATCH")

    def test_unsupported_tls_version_is_denied(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()), \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=FakeContext(FakeStream(tls_version="TLSv1.1"))):
            self.denied(PUBLIC, "UNACCEPTABLE_TLS_VERSION")

    def test_missing_peer_certificate_is_denied(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()), \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=FakeContext(FakeStream(cert={}))):
            self.denied(PUBLIC, "PEER_CERTIFICATE_UNVERIFIABLE")

    def test_expired_peer_certificate_is_denied(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()), \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=FakeContext(FakeStream(cert={
                              "notAfter": "Jan  1 00:00:00 2020 GMT"}))):
            self.denied(PUBLIC, "PEER_CERTIFICATE_EXPIRED")

    def test_certificate_verification_error_is_redacted(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          return_value=FakeStream()), \
             patch.object(preflight.ssl, "create_default_context",
                          return_value=FakeContext()) as ctx:
            ctx.return_value.wrap_socket = lambda *_a, **_kw: (
                _ for _ in ()).throw(ssl.SSLError("leaked private vendor host"))
            report = self.denied(PUBLIC, "TLS_TRANSPORT_UNVERIFIABLE")
        self.assertNotIn("leaked", json.dumps(report))

    def test_tcp_timeout_is_denied(self):
        with patch.object(preflight.socket, "getaddrinfo",
                          return_value=answers(GOOD)), \
             patch.object(preflight.socket, "create_connection",
                          side_effect=socket.timeout("timeout")):
            self.denied(PUBLIC, "TLS_TRANSPORT_UNVERIFIABLE")

    def test_default_report_cli_is_explicitly_unconfigured(self):
        with tempfile.TemporaryDirectory() as work:
            out = pathlib.Path(work) / "report.json"
            rc = preflight.main(["--purpose", "AUTHENTICATION_IDP",
                                 "--report-json", str(out)])
            self.assertEqual(rc, 0)
            report = json.loads(out.read_text())
            self.assertEqual(report["status"], "TARGET_NOT_CONFIGURED")
            self.assertFalse(report["national_release_allowed"])

    def test_no_host_ip_or_secret_can_appear_in_reports(self):
        report = preflight.inspect("AUTHENTICATION_IDP", "user:token@" + PUBLIC)
        self.assertNotIn("token", json.dumps(report))
        self.assertNotIn(PUBLIC, json.dumps(report))


if __name__ == "__main__":
    unittest.main()
