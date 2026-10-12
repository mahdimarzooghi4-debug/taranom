"""TRN-TECH-085: bounded, real-protocol but network-free FHIR technical probe tests."""
import io
import json
import os
from pathlib import Path
import socket
import ssl
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT / "tools/policy_checks"))
import check_private_fhir_capability as fhir

HOST = "authorized-operator-fhir.example.org"
IP = "1.1.1.1"
TARGET = {"schema_version": 1, "scope": fhir.REQUEST_SCOPE,
          "host": HOST, "base_path": "/fhir"}
CAPABILITY = {
    "resourceType": "CapabilityStatement", "status": "active",
    "kind": "instance", "fhirVersion": "4.0.1",
    "rest": [{"mode": "server", "resource": [
        {"type":"Patient", "interaction":[{"code":"read"},{"code":"search-type"}]},
        {"type":"Observation","interaction":[{"code":"search-type"}]},
        {"type":"Coverage","interaction":[{"code":"read"}]}
    ]}]
}

def payload(**kw):
    return json.dumps({**CAPABILITY,**kw}).encode()

class DummyTcp:
    def __enter__(self): return self
    def __exit__(self,*_): return False

class MockTls:
    def __init__(self,body, status="200 OK", content_type="application/fhir+json",
                 encoding="identity", more_headers=""):
        self.content=(f"HTTP/1.1 {status}\r\nContent-Type: {content_type}\r\n"
                      f"Content-Encoding: {encoding}\r\n"
                      f"Content-Length: {len(body)}\r\nConnection: close\r\n"
                      +more_headers+"\r\n").encode()+body
        self.sent=None
        self.timeout=None
    def __enter__(self): return self
    def __exit__(self,*_): return False
    def settimeout(self,value): self.timeout=value
    def getpeername(self): return (IP,443)
    def version(self): return "TLSv1.3"
    def getpeercert(self): return {"notAfter":"Dec 31 23:59:59 2099 GMT"}
    def sendall(self,data): self.sent=data
    def makefile(self,mode): return io.BytesIO(self.content)

class MockContext:
    def __init__(self,tls):
        self.tls=tls
        self.check_hostname=None
        self.verify_mode=None
        self.minimum_version=None
    def wrap_socket(self,raw,server_hostname):
        assert server_hostname==HOST
        assert self.check_hostname is True
        assert self.verify_mode==ssl.CERT_REQUIRED
        assert self.minimum_version==ssl.TLSVersion.TLSv1_2
        return self.tls

class FhirCapabilityTests(unittest.TestCase):
    def observed(self,content=payload(), target=TARGET):
        with patch.object(fhir.transport,"_safe_addresses",return_value=(IP,)):
            return fhir.inspect(target,fetcher=lambda host,ip,path:content)

    def denied(self,content,code,target=TARGET):
        result=self.observed(content,target)
        self.assertEqual(result["status"],"BLOCKED_UNVERIFIED")
        self.assertEqual(result["errors"],[code],result)
        self.assertFalse(result["fhir_data_exchange_verified"])
        self.assertFalse(result["release_allowed"])
        self.assertNotIn(HOST,json.dumps(result))

    def test_no_target_cannot_query_network(self):
        with patch.object(fhir.transport,"_safe_addresses",side_effect=AssertionError):
            out=fhir.inspect()
        self.assertEqual(out["status"],"TARGET_NOT_CONFIGURED")
        self.assertFalse(out["remote_resource_read_performed"])
        self.assertFalse(out["member_consent_or_identity_verified"])

    def test_genuine_advertisement_is_not_lawful_interoperability(self):
        out=self.observed()
        self.assertEqual(out["status"],"FHIR_CAPABILITY_SYNTAX_OBSERVED_NOT_AUTHORIZED")
        self.assertTrue(out["capability_advertisement_observed"])
        self.assertEqual(out["observed_version_advertisement_only"],"4.0.1")
        self.assertEqual(out["known_resource_types_advertised_only"],
                         ["Coverage","Observation","Patient"])
        self.assertEqual(out["advertised_interactions_only"]["Patient"],
                         ["read","search-type"])
        for flag in ("fhir_data_exchange_verified","remote_resource_read_performed",
                     "partner_contract_verified","clinical_permission_verified",
                     "interoperability_admitted","insurance_rights_verified",
                     "national_release_allowed","release_allowed"):
            self.assertFalse(out[flag])

    def test_strict_target_cannot_choose_dynamic_url_credentials_or_path(self):
        for patchval in ({"base_path":"/fhir/../secret"},
                         {"base_path":"/fhir?Patient=real"},
                         {"base_path":"//evil.example"},
                         {"base_path":"/fhir/%2e%2e"},
                         {"base_path":"https://evil.example"},
                         {"host":"127.0.0.1"}):
            with self.subTest(patchval=patchval):
                t={**TARGET,**patchval}
                with patch.object(fhir.transport,"_safe_addresses",side_effect=AssertionError):
                    out=fhir.inspect(t)
                self.assertEqual(out["status"],"BLOCKED_UNVERIFIED")
                self.assertTrue(out["errors"])

    def test_missing_or_invalid_fhir_metadata_fields(self):
        self.denied(payload(resourceType="Bundle"),"FHIR_CAPABILITY_RESOURCE_REQUIRED")
        self.denied(payload(fhirVersion="unknown"),"FHIR_ADVERTISED_VERSION_UNVERIFIABLE")
        self.denied(payload(status="approved"),"FHIR_CAPABILITY_STATUS_UNVERIFIABLE")
        self.denied(payload(kind="verified"),"FHIR_CAPABILITY_KIND_UNVERIFIABLE")
        self.denied(payload(rest=[]),"FHIR_SERVER_REST_CAPABILITIES_REQUIRED")
        self.denied(payload(rest=[{"mode":"client","resource":[{"type":"Patient"}]}]),
                    "FHIR_SERVER_REST_CAPABILITIES_REQUIRED")
        self.denied(payload(rest=[{"mode":"server","resource":[]}]),
                    "FHIR_SERVER_RESOURCES_UNVERIFIABLE")

    def test_fake_resource_types_and_bad_interactions_denied(self):
        self.denied(payload(rest=[{"mode":"server","resource":[{"type":"patient"}]}]),
                    "FHIR_RESOURCE_ADVERTISEMENT_UNVERIFIABLE")
        self.denied(payload(rest=[{"mode":"server","resource":[{"type":"Patient",
                    "interaction":[{"code":"ADMIN_APPROVED"}]}]}]),
                    "FHIR_INTERACTIONS_UNVERIFIABLE")
        self.denied(payload(rest=[{"mode":"server","resource":[{"type":"Patient",
                    "interaction":"read"}]}]),
                    "FHIR_INTERACTIONS_UNVERIFIABLE")

    def test_tricky_json_and_too_large_disallowed(self):
        self.denied(b'{"resourceType":"CapabilityStatement","resourceType":"Patient"}',
                    "FHIR_DUPLICATE_JSON_KEY")
        self.denied(b'{"status": NaN}',"FHIR_NONFINITE_JSON_VALUE")
        self.denied(b"{broken","FHIR_METADATA_JSON_UNVERIFIABLE")
        self.denied(b"x"*(fhir.MAX_BODY+1),"FHIR_RESPONSE_SIZE_UNVERIFIABLE")

    def test_dns_private_destination_blocked_before_socket(self):
        with patch.object(fhir.transport,"_safe_addresses",
                          side_effect=fhir.transport.ProbeError("NONPUBLIC_DNS_DESTINATION")),\
             patch.object(fhir.socket,"create_connection",side_effect=AssertionError):
            out=fhir.inspect(TARGET)
        self.assertEqual(out["errors"],["NONPUBLIC_DNS_DESTINATION"])

    def fetch_tls(self,tls):
        with patch.object(fhir.transport,"_safe_addresses",return_value=(IP,)),\
             patch.object(fhir.socket,"create_connection",return_value=DummyTcp()) as conn,\
             patch.object(fhir.ssl,"create_default_context",return_value=MockContext(tls)):
            out=fhir.inspect(TARGET)
        conn.assert_called_once_with((IP,443),timeout=5.0)
        return out

    def test_real_protocol_pinned_tls_exact_path_and_no_credentials(self):
        tls=MockTls(payload())
        out=self.fetch_tls(tls)
        self.assertTrue(out["capability_advertisement_observed"],out)
        self.assertEqual(tls.timeout,8.0)
        self.assertIn(b"GET /fhir/metadata HTTP/1.1",tls.sent)
        self.assertIn(("Host: "+HOST).encode(),tls.sent)
        self.assertIn(b"Accept: application/fhir+json",tls.sent)
        for unsafe in (b"Authorization:",b"Cookie:",b"POST ",b"/Patient",b"client_secret"):
            self.assertNotIn(unsafe,tls.sent)

    def test_redirect_nonjson_compression_and_header_abuse(self):
        for tls,code in (
            (MockTls(b"",status="302 Found"),"FHIR_HTTP_NOT_OK_OR_REDIRECT"),
            (MockTls(payload(),content_type="text/html"),"FHIR_UNEXPECTED_CONTENT_TYPE"),
            (MockTls(payload(),encoding="gzip"),"FHIR_COMPRESSED_RESPONSE_DENIED"),
            (MockTls(payload(),more_headers="X-Too-Large: "+"x"*fhir.MAX_HEADERS),
             "FHIR_HTTP_HEADERS_TOO_LARGE"),
        ):
            with self.subTest(code=code):
                self.assertEqual(self.fetch_tls(tls)["errors"],[code])

    def test_no_network_without_explicit_acknowledgement_and_private_file(self):
        with tempfile.TemporaryDirectory() as temp:
            pub=Path(temp)/"repo";pub.mkdir()
            private=Path(temp)/"private";private.mkdir(mode=0o700)
            target=private/"fhir-target.json"
            target.write_text(json.dumps(TARGET));target.chmod(0o600)
            report=private/"result.json"
            with patch.object(fhir.transport,"_safe_addresses",side_effect=AssertionError):
                code=fhir.main(["--root",str(pub),"--target-file",str(target),
                                "--report-json",str(report)])
            self.assertEqual(code,1)
            self.assertFalse(report.exists())

    def test_explicit_private_report_redacts_host_and_creates_exclusive_file(self):
        with tempfile.TemporaryDirectory() as temp:
            pub=Path(temp)/"repo";pub.mkdir()
            private=Path(temp)/"private";private.mkdir(mode=0o700)
            target=private/"fhir-target.json"
            target.write_text(json.dumps(TARGET));target.chmod(0o600)
            report=private/"result.json"
            with patch.object(fhir,"inspect",return_value=fhir.inspect()):
                code=fhir.main(["--root",str(pub),"--target-file",str(target),
                                "--acknowledge-authorized-technical-probe",
                                "--report-json",str(report)])
            self.assertEqual(code,0)
            self.assertEqual(report.stat().st_mode & 0o777,0o600)
            self.assertNotIn(HOST,report.read_text())
            self.assertFalse(json.loads(report.read_text())["release_allowed"])
            with patch.object(fhir,"inspect",side_effect=AssertionError):
                duplicate=fhir.main(["--root",str(pub),"--target-file",str(target),
                                    "--acknowledge-authorized-technical-probe",
                                    "--report-json",str(report)])
            self.assertEqual(duplicate,1)

    def test_public_report_path_and_symlink_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            pub=Path(temp)/"repo";pub.mkdir()
            private=Path(temp)/"private";private.mkdir(mode=0o700)
            target=private/"fhir.json";target.write_text(json.dumps(TARGET))
            target.chmod(0o600)
            with patch.object(fhir.transport,"_safe_addresses",side_effect=AssertionError):
                code=fhir.main(["--root",str(pub),"--target-file",str(target),
                    "--acknowledge-authorized-technical-probe",
                    "--report-json",str(pub/"leak.json")])
            self.assertEqual(code,1)
            symlink=private/"alias.json";symlink.symlink_to(target)
            code=fhir.main(["--root",str(pub),"--target-file",str(symlink),
                            "--acknowledge-authorized-technical-probe",
                            "--report-json",str(private/"new.json")])
            self.assertEqual(code,1)

    def test_no_target_ci_json_report_is_denied_by_default(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/"offline.json"
            with patch.object(fhir.transport,"_safe_addresses",side_effect=AssertionError):
                code=fhir.main(["--report-json",str(path)])
            self.assertEqual(code,0)
            report=json.loads(path.read_text())
            self.assertEqual(report["status"],"TARGET_NOT_CONFIGURED")
            self.assertFalse(report["fhir_data_exchange_verified"])
            self.assertFalse(report["national_release_allowed"])

if __name__=="__main__":
    unittest.main()
