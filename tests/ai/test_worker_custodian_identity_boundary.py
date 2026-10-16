"""TRN-TECH-160: identity/trust-root claims never establish outside custody."""
from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools"/"qa"))
import worker_custodian_identity_boundary as target
import worker_external_receipt_denial as receipt
import worker_evidence_anchor as anchor

class CustodianIdentityTests(unittest.TestCase):
    def setUp(self):
        self.claim = {
            "custodian_ref": "test:claimed-custodian-one",
            "administrative_domain_ref": target.TEST_DOMAIN,
            "trust_key_ref": target.TEST_KEY,
            "reviewer_ref": "test:claimed-independent-reviewer-one",
            "transfer_ref": target.TEST_TRANSFER,
            "challenge_ref": target.TEST_CHALLENGE,
            "claimed_status": "PRESENTED",
        }
        self.context = receipt.denied("STRUCTURAL_MATCH_UNAUTHENTICATED")
        self.context["internal_archive_shape_checked"] = True

    def evaluate(self,candidate,context=None):
        raw=anchor.canonical({"schema_version":1,"scope":target.SCOPE,
                              "candidate":candidate})
        return target.inspect(raw,self.context if context is None else context)

    def check_denial(self,report):
        self.assertEqual(target.SCOPE,report["scope"])
        for name in target.AUTHORITY_KEYS:
            self.assertIs(report[name],False,name)
        for v in self.claim.values():
            self.assertNotIn(v,json.dumps(report))

    def test_empty_canonical_template_denies_identity(self):
        raw=(ROOT/"docs/governance/ai-worker-custodian-identity-template.json").read_bytes()
        actual=target.inspect(raw,self.context)
        self.assertEqual("EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED",
                         actual["status"])
        self.check_denial(actual)

    def test_complete_matching_synthetic_claim_requires_review(self):
        outcome=self.evaluate(self.claim)
        self.assertEqual("IDENTITY_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",
                         outcome["status"])
        self.assertTrue(outcome["synthetic_archive_shape_checked"])
        self.check_denial(outcome)

    def test_even_matching_identity_without_external_receipt_is_denied(self):
        context=receipt.denied("EXTERNAL_RECEIPT_NOT_SUPPLIED")
        context["internal_archive_shape_checked"]=True
        r=self.evaluate(self.claim,context)
        self.assertEqual("DENIED_NO_AUTHENTICATED_OUTSIDE_RECEIPT",r["status"])
        self.check_denial(r)

    def test_external_authority_flag_spoofing_in_receipt_context_rejected(self):
        for key in ("external_receipt_verified","independent_custody_verified",
                    "real_custodian_identity_verified","human_release_approved",
                    "production_ready","national_release_allowed"):
            ctx={**self.context,key:True}
            r=self.evaluate(self.claim,ctx)
            self.assertEqual("DENIED_UNTRUSTED_RECEIPT_CONTEXT",r["status"],key)
            self.assertFalse(r["synthetic_archive_shape_checked"])
            self.check_denial(r)
        ctx={**self.context,"status":"VERIFIED"}
        self.assertEqual("DENIED_UNTRUSTED_RECEIPT_CONTEXT",
                         self.evaluate(self.claim,ctx)["status"])

    def test_replayed_transfer_or_challenge_fail(self):
        for name,status,altered in (
            ("transfer_ref","DENIED_TRANSFER_SCOPE_OR_REPLAY",
             "test:other-transfer-sequence-one"),
            ("challenge_ref","DENIED_CHALLENGE_REPLAY_OR_MISMATCH",
             "test:replayed-previous-challenge"),
        ):
            r=self.evaluate({**self.claim,name:altered})
            self.assertEqual(status,r["status"])
            self.check_denial(r)

    def test_cross_domain_and_changed_key_reference_denied(self):
        for name,status,value in (
            ("administrative_domain_ref","DENIED_ADMIN_DOMAIN_MISMATCH",
             "test:other-administrative-domain"),
            ("trust_key_ref","DENIED_KEY_REFERENCE_MISMATCH",
             "test:forged-key-reference-one"),
        ):
            r=self.evaluate({**self.claim,name:value})
            self.assertEqual(status,r["status"])
            self.check_denial(r)

    def test_fake_signature_approval_or_registered_authority_denied(self):
        for name in ("signature","issuer_verified","verified","approved_by",
                     "production_ready","release_allowed","key_material",
                     "key_revocation_approved","organizational_role"):
            r=self.evaluate({**self.claim,name:"test:attacker-authority-assertion"})
            self.assertEqual("DENIED_INVALID_IDENTITY_CLAIM",r["status"],name)
            self.check_denial(r)

    def test_invalid_claim_status_and_unsafe_identifiers_denied(self):
        for key,val in (
            ("claimed_status","APPROVED"),
            ("claimed_status","VERIFIED"),
            ("custodian_ref","https://provider.example/test"),
            ("reviewer_ref","token:shared-secret"),
            ("trust_key_ref","file:/tmp/key"),
            ("custodian_ref",True),
            ("challenge_ref","../"),
        ):
            r=self.evaluate({**self.claim,key:val})
            self.assertEqual("DENIED_INVALID_IDENTITY_CLAIM",r["status"],key)
            self.check_denial(r)

    def test_missing_fields_wrong_schema_and_unknown_scope_denied(self):
        r=self.evaluate({k:v for k,v in self.claim.items() if k!="reviewer_ref"})
        self.assertEqual("DENIED_INVALID_IDENTITY_CLAIM",r["status"])
        for schema,scope in ((2,target.SCOPE),(1,"PRODUCTION_TRUST_REGISTRY")):
            r=target.inspect(anchor.canonical({"schema_version":schema,
                             "scope":scope,"candidate":None}),self.context)
            self.assertEqual("DENIED_INVALID_IDENTITY_CLAIM",r["status"])
            self.check_denial(r)

    def test_duplicate_fields_nonfinite_binary_and_oversize_denied(self):
        for raw in (b'{"schema_version":1,"schema_version":1}',
                    b'{"schema_version":NaN}',b"\xff",b"*"*(target.MAX_INPUT+1)):
            r=target.inspect(raw,self.context)
            self.assertEqual("DENIED_INVALID_IDENTITY_CLAIM",r["status"])
            self.check_denial(r)

    def test_no_valid_prior_archive_or_receipt_denies_any_identity(self):
        for ctx in (
            receipt.denied("STRUCTURAL_MATCH_UNAUTHENTICATED"),
            None,{},{"status":"STRUCTURAL_MATCH_UNAUTHENTICATED"},
            {**self.context,"unexpected":"extra"},
            {**self.context,"internal_archive_shape_checked":False},
            {**self.context,"real_custodian_identity_verified":True},
        ):
            r=target.inspect(anchor.canonical({"schema_version":1,
                  "scope":target.SCOPE,"candidate":None}),ctx)
            self.assertEqual("DENIED_UNTRUSTED_RECEIPT_CONTEXT",r["status"])
            self.check_denial(r)

if __name__=="__main__":
    unittest.main()
