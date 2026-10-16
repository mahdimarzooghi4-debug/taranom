"""TRN-TECH-159: negative receipts cannot self-authenticate external custody."""
from __future__ import annotations
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools"/"qa"))
import worker_external_receipt_denial as target
import worker_evidence_handoff as handoff
import worker_evidence_anchor as anchor
from test_worker_evidence_anchor import chain

class ExternalReceiptTests(unittest.TestCase):
    def setUp(self):
        self.old="a"*32
        self.new="b"*32
        self.events=["Boot","Success","CapacityDenied","Boot","Success"]
        ledger=chain(self.events,[self.old]*3+[self.new]*2)
        rows=ledger.decode().splitlines()
        prefix=("\n".join(rows[:3])+"\n").encode()
        self.manifest={
            "contract":handoff.CONTRACT,
            "recordCount":5,"bootCount":2,
            "lastEpoch":self.new,
            "headDigest":json.loads(rows[-1])["digest"],
            "ledgerSha256":handoff.sha(ledger),
            "anchoredCount":3,
            "anchoredDigest":json.loads(rows[2])["digest"],
            "anchoredEpoch":self.old,
            "anchorPrefixSha256":handoff.sha(prefix),
            "auditEvidence":False,
            "independentCustody":False,
            "productionReady":False,
        }
        self.manifest_bytes=anchor.canonical(self.manifest)+b"\n"
        self.archive=handoff.stored_zip(ledger,self.manifest_bytes)
        self.claim={
            "transfer_ref":target.EXPECTED_TEST_TRANSFER,
            "archive_sha256":handoff.sha(self.archive),
            "manifest_sha256":handoff.sha(self.manifest_bytes),
            "ledger_head_digest":self.manifest["headDigest"],
            "anchored_count":3,
            "claimed_disposition":"RECEIVED",
        }

    def inspect(self,claim,archive=None):
        candidate={"schema_version":1,"scope":target.SCOPE,"claim":claim}
        return target.inspect(archive or self.archive,anchor.canonical(candidate))

    def denied(self,report):
        for key in ("external_receipt_verified","independent_custody_verified",
                    "real_custodian_identity_verified",
                    "retention_and_privacy_authorized",
                    "human_release_approved","production_ready",
                    "national_release_allowed"):
            self.assertIs(report[key],False,key)
        for private in (target.EXPECTED_TEST_TRANSFER,
                        "test:worker-synthetic-handoff-one","Bearer",
                        "patient","custodian.key"):
            self.assertNotIn(private,json.dumps(report))

    def test_empty_template_with_real_archive(self):
        raw=(ROOT/"docs/governance/ai-worker-external-receipt-template.json").read_bytes()
        r=target.inspect(self.archive,raw)
        self.assertEqual("EXTERNAL_RECEIPT_NOT_SUPPLIED",r["status"])
        self.assertTrue(r["internal_archive_shape_checked"])
        self.denied(r)

    def test_all_fields_equal_are_still_untrusted(self):
        r=self.inspect(self.claim)
        self.assertEqual("STRUCTURAL_MATCH_UNAUTHENTICATED",r["status"])
        self.assertTrue(r["internal_archive_shape_checked"])
        self.denied(r)

    def test_digest_substitution_and_head_anchor_changes_denied(self):
        for key,status in (
            ("archive_sha256","DENIED_RECEIPT_ARCHIVE_MISMATCH"),
            ("manifest_sha256","DENIED_RECEIPT_MANIFEST_MISMATCH"),
            ("ledger_head_digest","DENIED_RECEIPT_HEAD_MISMATCH")):
            altered={**self.claim,key:"0"*64}
            r=self.inspect(altered)
            self.assertEqual(status,r["status"],key)
            self.denied(r)
        r=self.inspect({**self.claim,"anchored_count":4})
        self.assertEqual("DENIED_RECEIPT_ANCHOR_MISMATCH",r["status"])
        self.denied(r)

    def test_transfer_replay_denied_even_if_all_hashes_match(self):
        altered={**self.claim,"transfer_ref":"test:worker-other-transfer-two"}
        r=self.inspect(altered)
        self.assertEqual("DENIED_RECEIPT_REPLAY_OR_SCOPE",r["status"])
        self.denied(r)

    def test_claim_cannot_supply_signed_status_or_fake_approval(self):
        for extrafield in ("signature","issuer","verified","independentCustody",
                           "production_ready","approved_by","release_allowed"):
            r=self.inspect({**self.claim,extrafield:True})
            self.assertEqual("DENIED_INVALID_RECEIPT_CLAIM",r["status"])
            self.denied(r)

    def test_invalid_fields_scope_schema_duplicate_json_nan(self):
        for invalid in (
            {**self.claim,"claimed_disposition":"APPROVED"},
            {**self.claim,"transfer_ref":"https://invalid.example/receipt"},
            {**self.claim,"archive_sha256":"g"*64},
            {**self.claim,"anchored_count":True},
            {**self.claim,"anchored_count":4097},
        ):
            r=self.inspect(invalid)
            self.assertEqual("DENIED_INVALID_RECEIPT_CLAIM",r["status"])
            self.denied(r)
        for raw in (
            b'{"schema_version":1,"schema_version":1}',
            b'{"schema_version":NaN}',
            b'{"schema_version":1,"scope":"PRODUCTION","claim":null}',
            b"\xff",
            b"*"*(target.INPUT_LIMIT+1),
        ):
            r=target.inspect(self.archive,raw)
            self.assertEqual("DENIED_INVALID_RECEIPT_CLAIM",r["status"])
            self.denied(r)

    def test_bad_archive_even_without_external_claim_fails_closed(self):
        candidate={"schema_version":1,"scope":target.SCOPE,"claim":None}
        raw=anchor.canonical(candidate)
        for bad in (b"not-a-zip", self.archive[:-13],b"x"*(handoff.ZIP_MAX+1)):
            r=target.inspect(bad,raw)
            self.assertEqual("DENIED_INVALID_SYNTHETIC_ARCHIVE",r["status"])
            self.denied(r)

    def test_rehashed_history_with_synthetic_claim_still_not_authority(self):
        original=handoff.open_zip(self.archive)[0]
        modified=chain(["Boot","ProcessRejected","CapacityDenied","Boot","Success"],
                       [self.old]*3+[self.new]*2)
        self.assertNotEqual(original,modified)
        rows=modified.decode().splitlines()
        m={**self.manifest,
            "anchoredDigest":json.loads(rows[2])["digest"],
            "headDigest":json.loads(rows[-1])["digest"],
            "ledgerSha256":handoff.sha(modified),
            "anchorPrefixSha256":handoff.sha(("\n".join(rows[:3])+"\n").encode())}
        manifest=anchor.canonical(m)+b"\n"
        archive=handoff.stored_zip(modified,manifest)
        forged={**self.claim,"archive_sha256":handoff.sha(archive),
                "manifest_sha256":handoff.sha(manifest),
                "ledger_head_digest":m["headDigest"]}
        r=self.inspect(forged,archive)
        self.assertEqual("STRUCTURAL_MATCH_UNAUTHENTICATED",r["status"])
        self.denied(r)
        # The prior independent HMAC anchor in Sprint 146 remains the
        # separate control against an owner re-hashing this entire history.

if __name__=="__main__":
    unittest.main()
