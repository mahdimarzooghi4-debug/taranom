"""TRN-TECH-083: private OIDC metadata read, DNS pin, TLS and hostile responses."""
import io
import json
from pathlib import Path
import socket
import ssl
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/policy_checks"))
import check_private_oidc_discovery as oidc

HOST = "idp.operator-supplied.org"
IP = "1.1.1.1"
DISCOVERY = {
    "issuer": "https://" + HOST,
    "authorization_endpoint": "https://" + HOST + "/authorize",
    "token_endpoint": "https://" + HOST + "/token",
    "jwks_uri": "https://" + HOST + "/jwks",
    "response_types_supported": ["code"],
    "subject_types_supported": ["public"],
    "id_token_signing_alg_values_supported": ["RS256"],
    "code_challenge_methods_supported": ["S256"]
}


def payload(**overrides):
    return json.dumps({**DISCOVERY, **overrides}).encode()


class DummyRaw:
    def __enter__(self): return self
    def __exit__(self, *_): return False


class FakeTls:
    def __init__(self, raw: bytes, status="200 OK", media="application/json"):
        self.raw = (f"HTTP/1.1 {status}\r\nContent-Type: {media}\r\n"
                    f"Content-Length: {len(raw)}\r\nConnection: close\r\n\r\n").encode() + raw
        self.sent = None
    def __enter__(self): return self
    def __exit__(self, *_): return False
    def getpeername(self): return (IP, 443)
    def version(self): return "TLSv1.3"
    def getpeercert(self): return {"notAfter": "Dec 31 23:59:59 2099 GMT"}
    def sendall(self, data): self.sent = data
    def makefile(self, mode): return io.BytesIO(self.raw)


class FakeContext:
    def __init__(self, tls):
        self.tls = tls
        self.check_hostname = None
        self.verify_mode = None
        self.minimum_version = None
    def wrap_socket(self, raw, server_hostname):
        assert server_hostname == HOST
        assert self.check_hostname is True
        assert self.verify_mode == ssl.CERT_REQUIRED
        assert self.minimum_version == ssl.TLSVersion.TLSv1_2
        return self.tls


def dns():
    return [(socket.AF_INET,socket.SOCK_STREAM,socket.IPPROTO_TCP,"",(IP,443))]


class OidcDiscoveryTests(unittest.TestCase):
    def inspected(self, data=payload()):
        with patch.object(oidc.transport, "_safe_addresses", return_value=(IP,)):
            return oidc.inspect(HOST, fetcher=lambda host,ip: data)

    def denied(self, raw, error):
        r = self.inspected(raw)
        self.assertEqual(r["status"], "BLOCKED_UNVERIFIED", r)
        self.assertEqual(r["errors"], [error], r)
        self.assertFalse(r["member_authentication_verified"])
        self.assertFalse(r["national_release_allowed"])
        self.assertNotIn(HOST, json.dumps(r))

    def test_unconfigured_is_no_network_and_no_fake_identity(self):
        with patch.object(oidc.transport, "_safe_addresses",side_effect=AssertionError):
            r = oidc.inspect()
        self.assertEqual(r["status"], "TARGET_NOT_CONFIGURED")
        self.assertFalse(r["read_only_metadata_observed"])
        self.assertFalse(r["browser_authentication_enabled"])

    def test_valid_bound_metadata_remains_not_authentication(self):
        r=self.inspected()
        self.assertEqual(r["status"], "DISCOVERY_SYNTAX_OBSERVED_NOT_AUTHORIZED")
        self.assertTrue(r["read_only_metadata_observed"])
        for key in ("member_authentication_verified", "external_provider_contract_verified",
                    "jwks_fetched_or_verified", "id_token_signature_verified",
                    "member_activation_allowed", "release_allowed"):
            self.assertFalse(r[key])

    def test_issuer_mismatch_and_attacker_cross_host(self):
        self.denied(payload(issuer="https://bad.operator-supplied.org"),
                    "ISSUER_BINDING_MISMATCH")
        self.denied(payload(token_endpoint="https://evil.org/token"),
                    "UNTRUSTED_DISCOVERY_ENDPOINT")
        self.denied(payload(jwks_uri="http://" + HOST + "/jwks"),
                    "UNTRUSTED_DISCOVERY_ENDPOINT")
        self.denied(payload(jwks_uri="https://" + HOST + ":444/jwks"),
                    "UNTRUSTED_DISCOVERY_ENDPOINT")

    def test_unsafe_endpoints_userinfo_port_paths(self):
        for url in ("https://user:pass@" + HOST + "/jwks",
                    "https://" + HOST + "/jwks?token=secret",
                    "https://" + HOST + "/jwks#frag",
                    "https://" + HOST + "/",
                    "//" + HOST + "/jwks"):
            with self.subTest(url=url):
                self.denied(payload(jwks_uri=url),"UNTRUSTED_DISCOVERY_ENDPOINT")

    def test_no_code_or_pkce_or_asymmetric_signature(self):
        self.denied(payload(response_types_supported=["id_token"]),
                    "AUTHORIZATION_CODE_FLOW_NOT_ADVERTISED")
        self.denied(payload(code_challenge_methods_supported=["plain"]),
                    "PKCE_S256_NOT_ADVERTISED")
        self.denied(payload(id_token_signing_alg_values_supported=["none"]),
                    "APPROVED_ALGORITHM_NOT_ADVERTISED")
        self.denied(payload(id_token_signing_alg_values_supported=["RS256","none"]),
                    "UNSIGNED_TOKENS_ADVERTISED")

    def test_missing_fields_invalid_json_and_duplicate_keys(self):
        self.denied(payload(authorization_endpoint=None),
                    "UNTRUSTED_DISCOVERY_ENDPOINT")
        self.denied(b"{bad json", "DISCOVERY_JSON_UNVERIFIABLE")
        self.denied(b'{"issuer":"a","issuer":"b"}', "DUPLICATE_DISCOVERY_JSON_KEY")
        self.denied(b"{}", "ISSUER_BINDING_MISMATCH")
        self.denied(b"x"*(oidc.MAX_METADATA_BYTES+1),"DISCOVERY_SIZE_UNVERIFIABLE")

    def test_safe_dns_and_tls_request_fixed_path_no_redirect(self):
        tls=FakeTls(payload())
        with patch.object(oidc.transport, "_safe_addresses",return_value=(IP,)), \
             patch.object(oidc.socket, "create_connection",return_value=DummyRaw()) as connector, \
             patch.object(oidc.ssl, "create_default_context",return_value=FakeContext(tls)):
            r=oidc.inspect(HOST)
        self.assertTrue(r["read_only_metadata_observed"],r)
        connector.assert_called_once_with((IP,443), timeout=5.0)
        self.assertIn(b"GET /.well-known/openid-configuration HTTP/1.1",tls.sent)
        self.assertIn(("Host: "+HOST).encode(),tls.sent)
        self.assertNotIn(b"Authorization:",tls.sent)
        self.assertNotIn(b"Cookie:",tls.sent)

    def test_redirect_denied_without_following_remote(self):
        tls=FakeTls(b"",status="302 Found")
        with patch.object(oidc.transport, "_safe_addresses",return_value=(IP,)), \
             patch.object(oidc.socket,"create_connection",return_value=DummyRaw()), \
             patch.object(oidc.ssl,"create_default_context",return_value=FakeContext(tls)):
            r=oidc.inspect(HOST)
        self.assertEqual(r["errors"],["DISCOVERY_HTTP_NOT_OK_OR_REDIRECT"])

    def test_non_json_body_denied(self):
        tls=FakeTls(payload(),media="text/html")
        with patch.object(oidc.transport,"_safe_addresses",return_value=(IP,)), \
             patch.object(oidc.socket,"create_connection",return_value=DummyRaw()), \
             patch.object(oidc.ssl,"create_default_context",return_value=FakeContext(tls)):
            r=oidc.inspect(HOST)
        self.assertEqual(r["errors"],["DISCOVERY_CONTENT_TYPE_UNVERIFIABLE"])

    def test_private_dns_denied_before_tls(self):
        with patch.object(oidc.transport, "_safe_addresses",
                          side_effect=oidc.transport.ProbeError("NONPUBLIC_DNS_DESTINATION")), \
             patch.object(oidc.socket,"create_connection",side_effect=AssertionError):
            r=oidc.inspect(HOST)
        self.assertEqual(r["errors"],["NONPUBLIC_DNS_DESTINATION"])

    def test_private_cli_only_owner_private_target_and_report(self):
        with tempfile.TemporaryDirectory() as home:
            base=Path(home)
            pub=base/"public"
            pub.mkdir()
            private=base/"private"
            private.mkdir(mode=0o700)
            target=private/"target.txt"
            target.write_text(HOST+"\n")
            target.chmod(0o600)
            report=private/"result.json"
            with patch.object(oidc,"inspect", return_value=oidc.inspect()):
                rc=oidc.main(["--root",str(pub),"--target-file",str(target),
                              "--report-json",str(report)])
            self.assertEqual(rc,0)
            self.assertEqual(report.stat().st_mode & 0o777,0o600)
            self.assertNotIn(HOST, report.read_text())
            self.assertEqual(report.read_text().count('"release_allowed": false'),1)

    def test_public_output_rejected_without_network(self):
        with tempfile.TemporaryDirectory() as home:
            base=Path(home)
            pub=base/"public"
            pub.mkdir()
            target=base/"idp.txt"
            target.write_text(HOST)
            target.chmod(0o600)
            with patch.object(oidc.transport, "_safe_addresses",side_effect=AssertionError):
                rc=oidc.main(["--root",str(pub),"--target-file",str(target),
                              "--report-json",str(pub/"report.json")])
            self.assertEqual(rc,1)
            self.assertFalse((pub/"report.json").exists())

    def test_default_ci_mode_produces_denied_report(self):
        with tempfile.TemporaryDirectory() as home:
            path=Path(home)/"ci.json"
            rc=oidc.main(["--report-json",str(path)])
            self.assertEqual(rc,0)
            report=json.loads(path.read_text())
            self.assertEqual(report["status"],"TARGET_NOT_CONFIGURED")
            self.assertFalse(report["national_release_allowed"])


if __name__=="__main__":
    unittest.main()
