"""TRN-TECH-158: external-custody claims cannot self-promote to authority."""
from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools"/"qa"))
import check_worker_external_custody as target

class CustodyContractTests(unittest.TestCase):
    def setUp(self):
        self.claim={k:"synthetic:review-"+str(i)+"-reference" for i,k in enumerate(target.TOPICS)}
        self.base={"schema_version":1,"scope":target.SCOPE,"candidate":None}

    def check(self,obj):
        return target.inspect_bytes(json.dumps(obj).encode())

    def denied(self,report):
        for k in ("independent_custody_verified","external_receipt_verified",
                  "retention_and_privacy_authorized","human_release_approved",
                  "production_ready","national_release_allowed"):
            self.assertIs(report[k],False,k)
        self.assertNotIn("synthetic:review-",json.dumps(report))
        self.assertEqual(target.SCOPE,report["scope"])

    def test_repo_template_is_non_authorizing(self):
        raw=(ROOT/"docs/governance/ai-worker-external-custody-template.json").read_bytes()
        r=target.inspect_bytes(raw)
        self.assertEqual("EXTERNAL_CUSTODY_NOT_CONFIGURED",r["status"])
        self.assertEqual(len(target.TOPICS),len(r["missing_topics"]))
        self.denied(r)

    def test_every_claim_is_still_not_independent_verification(self):
        self.base["candidate"]={"topic_refs":self.claim}
        r=self.check(self.base)
        self.assertEqual("EXTERNAL_CUSTODY_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",r["status"])
        self.assertEqual(len(target.TOPICS),r["candidate_topics_supplied"])
        self.assertEqual([],r["missing_topics"])
        self.denied(r)

    def test_missing_topics_and_opaque_refs(self):
        self.base["candidate"]={"topic_refs":{"external_receipt_provenance": "synthetic:test-ref"}}
        r=self.check(self.base)
        self.assertEqual("EXTERNAL_CUSTODY_CLAIMS_INCOMPLETE",r["status"])
        self.assertEqual(1,r["candidate_topics_supplied"])
        self.assertIn("distinct_administrative_control",r["missing_topics"])
        self.denied(r)

    def test_explicit_fake_verification_status_and_authority_fields_denied(self):
        for extra in ({"independent_custody_verified":True},
                      {"production_ready":True},{"approved_by":"admin"},
                      {"review_state":"APPROVED"}):
            self.base["candidate"]={"topic_refs":self.claim,**extra}
            r=self.check(self.base)
            self.assertEqual("DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",r["status"])
            self.denied(r)

    def test_invalid_reference_values_rejected_without_echo(self):
        for val in ("http://fake.local/claim","file:/tmp/artifact",
                    "token:shadow-reference", "../oops", "x", "", None, True):
            self.base["candidate"]={"topic_refs":{"external_receipt_provenance":val}}
            r=self.check(self.base)
            self.assertEqual("DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",r["status"],val)
            self.denied(r)

    def test_unknown_topic_and_unsupported_scopes(self):
        self.base["candidate"]={"topic_refs":{"HUMAN_APPROVE":"synthetic:approval"}}
        self.denied(self.check(self.base))
        self.assertEqual("DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",self.check(self.base)["status"])
        self.base={"schema_version":2,"scope":target.SCOPE,"candidate":None}
        self.assertEqual("DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",self.check(self.base)["status"])
        self.base={"schema_version":1,"scope":"PRODUCTION_RELEASE","candidate":None}
        self.assertEqual("DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",self.check(self.base)["status"])

    def test_duplicate_json_keys_nonfinite_and_oversized_denied(self):
        for raw in (b'{"schema_version":1,"schema_version":1}',
                    b'{"schema_version":NaN}',
                    b'{"candidate":{"topic_refs":{"x":1,"x":2}}}',
                    b"\xff"+b"x"*3,b"x"*target.MAX_BYTES+b"x"):
            r=target.inspect_bytes(raw)
            self.assertEqual("DENIED_INVALID_EXTERNAL_CUSTODY_CLAIM",r["status"])
            self.denied(r)

    def test_report_is_new_only_and_owner_private(self):
        with tempfile.TemporaryDirectory() as d:
            output=Path(d)/"report.json"
            report=self.check(self.base)
            target._write_denial_only(output,report)
            self.assertEqual(0o600,output.stat().st_mode & 0o777)
            self.assertEqual(report,json.loads(output.read_bytes()))
            with self.assertRaises(FileExistsError):
                target._write_denial_only(output,report)
            self.assertEqual(report,json.loads(output.read_bytes()))

    def test_report_symlink_cannot_replace_target(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            target_path=root/"existing"
            target_path.write_text("unmodified")
            link=root/"report"
            link.symlink_to(target_path)
            with self.assertRaises(target.InvalidCustodyContract):
                target._write_denial_only(link,self.check(self.base))
            self.assertEqual("unmodified",target_path.read_text())

    def test_input_symlink_denied(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            src=root/"manifest.json"
            src.write_text(json.dumps(self.base))
            symlink=root/"alias.json"
            symlink.symlink_to(src)
            with self.assertRaises(target.InvalidCustodyContract):
                target._secure_input(symlink)

if __name__=="__main__":
    unittest.main()
