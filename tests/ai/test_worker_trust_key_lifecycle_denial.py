"""TRN-TECH-161: untrusted lifecycle sequence/revocation never promotes keys."""
from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools"/"qa"))
import worker_trust_key_lifecycle_denial as target
import worker_custodian_identity_boundary as identity
import worker_evidence_anchor as anchor

class TrustKeyLifecycleTests(unittest.TestCase):
    def setUp(self):
        self.old = "test:prior-synthetic-key-one"
        self.new = "test:replacement-synthetic-key-two"
        self.other = "test:unrecognized-synthetic-key-three"
        self.ctx = identity.denied("EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED",True)
        self.first = {"sequence":1,"event_ref":"test:event-initial-one",
                      "key_ref":self.old,"kind":"PRESENTED",
                      "previous_key_ref":None}
        self.rotate = {"sequence":2,"event_ref":"test:event-rotation-two",
                       "key_ref":self.new,"kind":"ROTATION_CLAIMED",
                       "previous_key_ref":self.old}
        self.candidate = {
            "events":[self.first,self.rotate],
            "attempted_key_ref":self.new,
            "transfer_ref":identity.TEST_TRANSFER,
            "challenge_ref":identity.TEST_CHALLENGE,
        }

    def run_claim(self, candidate, ctx=None):
        data={"schema_version":1,"scope":target.SCOPE,"candidate":candidate}
        return target.inspect(anchor.canonical(data),
                              self.ctx if ctx is None else ctx)

    def all_denied(self,r):
        self.assertEqual(target.SCOPE,r["scope"])
        for k in target.FLAGS:
            self.assertIs(r[k],False,k)
        for secret in (self.old,self.new,self.other,"test:event-initial-one",
                       "test:event-rotation-two","patient","Bearer"):
            self.assertNotIn(secret,json.dumps(r))

    def test_empty_public_template_preserves_denial(self):
        raw=(ROOT/"docs/governance/ai-worker-trust-key-lifecycle-template.json").read_bytes()
        r=target.inspect(raw,self.ctx)
        self.assertEqual("EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED",r["status"])
        self.assertTrue(r["synthetic_archive_shape_checked"])
        self.all_denied(r)

    def test_consistent_rotation_claim_is_never_verified(self):
        r=self.run_claim(self.candidate)
        self.assertEqual("KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",r["status"])
        self.all_denied(r)

    def test_old_key_after_rotation_denied(self):
        r=self.run_claim({**self.candidate,"attempted_key_ref":self.old})
        self.assertEqual("DENIED_SUPERSEDED_KEY",r["status"])
        self.all_denied(r)

    def test_revoked_key_never_reactivates(self):
        revoke={"sequence":2,"event_ref":"test:event-revoke-two",
                "key_ref":self.old,"kind":"REVOKED","previous_key_ref":None}
        candidate={**self.candidate,"events":[self.first,revoke],
                   "attempted_key_ref":self.old}
        r=self.run_claim(candidate)
        self.assertEqual("DENIED_REVOKED_OR_RETIRED_KEY",r["status"])
        self.all_denied(r)
        resurrect={"sequence":3,"event_ref":"test:event-restore-three",
                   "key_ref":self.old,"kind":"PRESENTED","previous_key_ref":None}
        r=self.run_claim({**candidate,"events":[self.first,revoke,resurrect]})
        self.assertEqual("DENIED_REACTIVATION_AFTER_TERMINATION",r["status"])
        self.all_denied(r)

    def test_retired_key_denied_even_with_claimed_successor(self):
        retire={"sequence":2,"event_ref":"test:event-retire-two",
                "key_ref":self.old,"kind":"RETIRED","previous_key_ref":None}
        r=self.run_claim({**self.candidate,"events":[self.first,retire],
                          "attempted_key_ref":self.old})
        self.assertEqual("DENIED_REVOKED_OR_RETIRED_KEY",r["status"])
        self.all_denied(r)
        r=self.run_claim({**self.candidate,"events":[self.first,retire],
                          "attempted_key_ref":self.new})
        self.assertEqual("DENIED_KEY_LIFECYCLE_TERMINATED",r["status"])
        self.all_denied(r)

    def test_unknown_key_and_unapproved_first_presentation(self):
        r=self.run_claim({**self.candidate,"attempted_key_ref":self.other})
        self.assertEqual("DENIED_UNKNOWN_OR_STALE_KEY",r["status"])
        self.all_denied(r)
        r=self.run_claim({**self.candidate,"events":[self.first],
                          "attempted_key_ref":self.old})
        self.assertEqual("KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",r["status"])
        self.all_denied(r)

    def test_rotation_predecessor_collision_and_key_reuse(self):
        for changed in (
            {**self.rotate,"previous_key_ref":self.other},
            {**self.rotate,"key_ref":self.old},
            {**self.rotate,"key_ref":self.other,"previous_key_ref":None},
        ):
            r=self.run_claim({**self.candidate,"events":[self.first,changed]})
            self.assertEqual("DENIED_INCONSISTENT_ROTATION_CLAIM",r["status"])
            self.all_denied(r)

    def test_replay_duplicate_event_or_sequence(self):
        for changed in (
            {**self.rotate,"sequence":1},
            {**self.rotate,"sequence":3},
            {**self.rotate,"sequence":True},
            {**self.rotate,"event_ref":self.first["event_ref"]},
        ):
            r=self.run_claim({**self.candidate,"events":[self.first,changed]})
            self.assertIn(r["status"],{
                "DENIED_INVALID_KEY_LIFECYCLE_CLAIM","DENIED_REPLAYED_EVENT"})
            self.all_denied(r)
        r=self.run_claim({**self.candidate,
            "events":[self.first,self.rotate,self.first]})
        self.assertEqual("DENIED_INVALID_KEY_LIFECYCLE_CLAIM",r["status"])
        self.all_denied(r)

    def test_cross_transfer_and_challenge_replay(self):
        for name in ("transfer_ref","challenge_ref"):
            r=self.run_claim({**self.candidate,
               name:"test:unrelated-previous-transaction"})
            self.assertEqual("DENIED_TRANSFER_OR_CHALLENGE_REPLAY",r["status"])
            self.all_denied(r)

    def test_unapproved_positive_status_fields_fail_closed(self):
        for k in ("verified","approved_by","signature","active","production_ready",
                  "revocation_verified","rotation_approved","key_material",
                  "independentCustody","revocation_authority"):
            r=self.run_claim({**self.candidate,k:True})
            self.assertEqual("DENIED_INVALID_KEY_LIFECYCLE_CLAIM",r["status"])
            self.all_denied(r)

    def test_invalid_event_fields_and_restored_presentation_denied(self):
        for changed in (
            {**self.rotate,"kind":"APPROVED"},
            {**self.rotate,"key_ref":"https://fake-key.example"},
            {**self.rotate,"previous_key_ref":"file:/tmp/key"},
            {**self.rotate,"sequence":"2"},
            {**self.rotate,"received_at":"synthetic:timestamp"},
        ):
            r=self.run_claim({**self.candidate,"events":[self.first,changed]})
            self.assertEqual("DENIED_INVALID_KEY_LIFECYCLE_CLAIM",r["status"])
            self.all_denied(r)

    def test_prior_identity_status_spoof_or_absence_fails(self):
        for bad in (
            None,{},identity.denied("IDENTITY_VERIFIED",True),
            identity.denied("EXTERNAL_CUSTODIAN_IDENTITY_NOT_CONFIGURED",False),
            {**self.ctx,"trust_anchor_verified":True},
            {**self.ctx,"status":[]},
            {**self.ctx,"status":{"bad":"value"}},
            {**self.ctx,"extra":"spoof"},
        ):
            raw=anchor.canonical({"schema_version":1,"scope":target.SCOPE,
                                  "candidate":self.candidate})
            r=target.inspect(raw,bad)
            self.assertEqual("DENIED_UNTRUSTED_IDENTITY_CONTEXT",r["status"])
            self.assertFalse(r["synthetic_archive_shape_checked"])
            self.all_denied(r)

    def test_malformed_scope_version_duplicates_and_oversize(self):
        for raw in (
            b'{"schema_version":1,"schema_version":1}',
            b'{"schema_version":NaN}',
            b'{"schema_version":2,"scope":"INVALID","candidate":null}',
            b"\xff",b"x"*(target.MAX_BYTES+1),
        ):
            r=target.inspect(raw,self.ctx)
            self.assertEqual("DENIED_INVALID_KEY_LIFECYCLE_CLAIM",r["status"])
            self.all_denied(r)

    def test_nonexistent_or_fake_rotation_claim_cannot_authorize_release(self):
        candidate={**self.candidate,"events":[self.first,self.rotate],
                   "attempted_key_ref":self.new}
        r=self.run_claim(candidate)
        self.assertEqual("KEY_LIFECYCLE_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",r["status"])
        self.assertFalse(r["production_ready"])
        self.assertFalse(r["national_release_allowed"])
        self.all_denied(r)

if __name__=="__main__":
    unittest.main()
