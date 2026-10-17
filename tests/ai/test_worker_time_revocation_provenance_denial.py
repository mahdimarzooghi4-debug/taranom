"""TRN-TECH-163: untrusted source assertion digest cannot impersonate signer."""
from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools"/"qa"))
import worker_time_revocation_provenance_denial as target
import worker_trust_temporal_denial as temporal
import worker_custodian_identity_boundary as identity
import worker_evidence_anchor as anchor

DEFAULT=object()
class ProvenanceDenialTests(unittest.TestCase):
    def setUp(self):
        self.context=temporal.denied("TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED",True)
        self.clock=self.stamp({
            "source_ref":"test:untrusted-clock-source-one",
            "assertion_ref":"test:clock-assertion-one",
            "claimed_utc":"2026-01-10T00:00:00Z"})
        self.notice=self.stamp({
            "source_ref":"test:untrusted-revocation-source-one",
            "assertion_ref":"test:notice-assertion-one",
            "key_ref":"test:synthetic-revoked-key-one",
            "kind":"REVOKED",
            "claimed_effective_at":"2026-01-08T00:00:00Z",
            "claimed_observed_at":"2026-01-09T00:00:00Z"})
        self.candidate={
            "transfer_ref":identity.TEST_TRANSFER,
            "challenge_ref":identity.TEST_CHALLENGE,
            "clock_claim":self.clock,"revocation_notices":[self.notice]}

    def stamp(self,body,transfer=identity.TEST_TRANSFER,
              challenge=identity.TEST_CHALLENGE):
        return {**body,"content_sha256":
                target.untrusted_digest(body,transfer,challenge)}

    def inspect(self,candidate=DEFAULT,ctx=DEFAULT):
        raw=anchor.canonical({"schema_version":target.SCHEMA,
                              "scope":target.SCOPE,
                              "candidate":self.candidate if candidate is DEFAULT else candidate})
        return target.inspect(raw,self.context if ctx is DEFAULT else ctx)

    def forbidden(self,result):
        self.assertEqual(target.SCOPE,result["scope"])
        for flag in target.FLAGS:
            self.assertIs(result[flag],False,flag)
        out=json.dumps(result)
        for forbidden in ("test:untrusted", "test:synthetic", "2026-01",
                          "Bearer","patient","signature"):
            self.assertNotIn(forbidden,out)

    def test_empty_template_remains_unconfigured(self):
        raw=(ROOT/"docs/governance/ai-worker-time-revocation-source-template.json").read_bytes()
        out=target.inspect(raw,self.context)
        self.assertEqual("TIME_REVOCATION_SOURCES_NOT_CONFIGURED",out["status"])
        self.assertTrue(out["synthetic_archive_shape_checked"])
        self.forbidden(out)

    def test_perfectly_matching_untrusted_digests_never_authenticate(self):
        out=self.inspect()
        self.assertEqual("SOURCE_CLAIMS_REQUIRE_INDEPENDENT_PROVENANCE_REVIEW",
                         out["status"])
        self.forbidden(out)

    def test_clock_and_revocation_digest_substitution(self):
        for field,expected in (("clock_claim","DENIED_TIME_CLAIM_DIGEST_MISMATCH"),
                               ("revocation_notices","DENIED_REVOCATION_NOTICE_DIGEST_MISMATCH")):
            if field=="clock_claim":
                altered={**self.candidate,"clock_claim":
                         {**self.clock,"content_sha256":"0"*64}}
            else:
                altered={**self.candidate,"revocation_notices":
                         [{**self.notice,"content_sha256":"0"*64}]}
            out=self.inspect(altered)
            self.assertEqual(expected,out["status"])
            self.forbidden(out)

    def test_claimant_can_rehash_modified_content_but_still_has_no_authority(self):
        c=self.stamp({**{k:v for k,v in self.clock.items() if k!="content_sha256"},
                      "claimed_utc":"2026-01-12T00:00:00Z"})
        notice=self.stamp({**{k:v for k,v in self.notice.items() if k!="content_sha256"},
                           "claimed_observed_at":"2026-01-11T00:00:00Z"})
        out=self.inspect({**self.candidate,"clock_claim":c,
                          "revocation_notices":[notice]})
        self.assertEqual("SOURCE_CLAIMS_REQUIRE_INDEPENDENT_PROVENANCE_REVIEW",
                         out["status"])
        self.forbidden(out)

    def test_transfer_challenge_replay(self):
        for k in ("transfer_ref","challenge_ref"):
            out=self.inspect({**self.candidate,k:"test:old-transfer-challenge"})
            self.assertEqual("DENIED_SOURCE_TRANSFER_OR_CHALLENGE_REPLAY",out["status"])
            self.forbidden(out)

    def test_duplicate_assertion_id_and_key_equivocation(self):
        collision=self.stamp({**{k:v for k,v in self.notice.items() if k!="content_sha256"},
                             "assertion_ref":self.clock["assertion_ref"]})
        out=self.inspect({**self.candidate,"revocation_notices":[collision]})
        self.assertEqual("DENIED_REPLAYED_SOURCE_ASSERTION",out["status"])
        self.forbidden(out)
        duplicate=self.stamp({**{k:v for k,v in self.notice.items() if k!="content_sha256"},
                             "assertion_ref":"test:notice-assertion-two",
                             "kind":"RETIRED",
                             "claimed_observed_at":"2026-01-09T12:00:00Z"})
        out=self.inspect({**self.candidate,"revocation_notices":[self.notice,duplicate]})
        self.assertEqual("DENIED_CONFLICTING_KEY_TERMINATION_CLAIMS",out["status"])
        self.forbidden(out)

    def test_revocation_effective_after_observed_is_incoherent(self):
        claim=self.stamp({**{k:v for k,v in self.notice.items() if k!="content_sha256"},
                          "claimed_effective_at":"2026-01-09T02:00:00Z",
                          "claimed_observed_at":"2026-01-09T01:00:00Z"})
        out=self.inspect({**self.candidate,"revocation_notices":[claim]})
        self.assertEqual("DENIED_TERMINATION_EFFECTIVE_AFTER_OBSERVED",out["status"])
        self.forbidden(out)

    def test_clock_asserted_earlier_than_revocation_observation(self):
        earlier=self.stamp({**{k:v for k,v in self.clock.items() if k!="content_sha256"},
                            "claimed_utc":"2026-01-08T00:00:00Z"})
        out=self.inspect({**self.candidate,"clock_claim":earlier})
        self.assertEqual("DENIED_CLOCK_NOTICE_TIME_CONTRADICTION",out["status"])
        self.forbidden(out)

    def test_out_of_order_observation_claims(self):
        second=self.stamp({**{k:v for k,v in self.notice.items() if k!="content_sha256"},
                           "assertion_ref":"test:notice-other-key-two",
                           "key_ref":"test:synthetic-other-key-two",
                           "claimed_effective_at":"2026-01-06T00:00:00Z",
                           "claimed_observed_at":"2026-01-08T00:00:00Z"})
        out=self.inspect({**self.candidate,"revocation_notices":[self.notice,second]})
        self.assertEqual("DENIED_SOURCE_NOTICE_OBSERVATION_ORDER",out["status"])
        self.forbidden(out)

    def test_unsupported_fake_signatures_and_verified_fields_rejected(self):
        for k in ("signature","signed_by","verified","approved_by","source_trusted",
                  "production_ready","independent_custody_verified","expiry_policy"):
            altered={**self.clock,k:"test:forged-proof"}
            out=self.inspect({**self.candidate,"clock_claim":altered})
            self.assertEqual("DENIED_INVALID_SOURCE_CLAIMS",out["status"],k)
            self.forbidden(out)

    def test_format_and_wrong_event_types_fail_closed(self):
        for field,value in (("claimed_utc","2026-01-10T00:00:00+00:00"),
                            ("claimed_utc","2026-02-30T00:00:00Z"),
                            ("claimed_utc",True),("claimed_utc",None),
                            ("source_ref","https://fake-clock.example")):
            out=self.inspect({**self.candidate,"clock_claim":{**self.clock,field:value}})
            self.assertEqual("DENIED_INVALID_SOURCE_CLAIMS",out["status"])
            self.forbidden(out)
        out=self.inspect({**self.candidate,"revocation_notices":[
            {**self.notice,"kind":"APPROVED"}]})
        self.assertEqual("DENIED_INVALID_SOURCE_CLAIMS",out["status"])
        self.forbidden(out)

    def test_untrusted_or_positive_prior_context_is_blocked(self):
        for bad in (None,{},temporal.denied("TEMPORAL_VERIFIED",True),
                    temporal.denied("TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED",False),
                    {**self.context,"trusted_clock_verified":True},
                    {**self.context,"status":[]},{**self.context,"extra":True}):
            out=self.inspect(ctx=bad)
            self.assertEqual("DENIED_UNTRUSTED_TEMPORAL_CONTEXT",out["status"])
            self.assertFalse(out["synthetic_archive_shape_checked"])
            self.forbidden(out)

    def test_duplicate_keys_unsupported_scope_and_size_denied(self):
        for raw in (b'{"schema_version":1,"schema_version":1}',
                    b'{"schema_version":NaN}',b"\xff",b"x"*(target.MAX_BYTES+1),
                    b'{"schema_version":1,"scope":"PRODUCTION","candidate":null}'):
            out=target.inspect(raw,self.context)
            self.assertEqual("DENIED_INVALID_SOURCE_CLAIMS",out["status"])
            self.forbidden(out)

    def test_empty_list_and_non_dict_claims_denied(self):
        for notices in ([],{},None,"REVOKED",True,list(range(7))):
            out=self.inspect({**self.candidate,"revocation_notices":notices})
            self.assertEqual("DENIED_INVALID_SOURCE_CLAIMS",out["status"])
            self.forbidden(out)

if __name__=="__main__":
    unittest.main()
