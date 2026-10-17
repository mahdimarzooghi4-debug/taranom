"""TRN-TECH-162: synthetic key time cannot confer clock/expiry authority."""
from __future__ import annotations

import json
from pathlib import Path
import sys
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools"/"qa"))
import worker_trust_temporal_denial as target
import worker_trust_key_lifecycle_denial as lifecycle
import worker_custodian_identity_boundary as identity
import worker_evidence_anchor as anchor

class SyntheticTemporalTests(unittest.TestCase):
    def setUp(self):
        self.old="test:old-key-for-time-claim-one"
        self.new="test:new-key-for-time-claim-two"
        self.ctx=lifecycle.denied("EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED",True)
        self.events=[
            {"sequence":1,"event_ref":"test:time-event-introduced-one",
             "key_ref":self.old,"kind":"PRESENTED","previous_key_ref":None,
             "claimed_effective_at":"2026-01-01T00:00:00Z",
             "claimed_observed_at":"2026-01-01T01:00:00Z"},
            {"sequence":2,"event_ref":"test:time-event-rotated-two",
             "key_ref":self.new,"kind":"ROTATION_CLAIMED",
             "previous_key_ref":self.old,
             "claimed_effective_at":"2026-01-05T00:00:00Z",
             "claimed_observed_at":"2026-01-05T01:00:00Z"}
        ]
        self.windows=[
            {"key_ref":self.old,"claimed_not_before":"2026-01-01T00:00:00Z",
             "claimed_expires_at":"2026-01-20T00:00:00Z"},
            {"key_ref":self.new,"claimed_not_before":"2026-01-05T00:00:00Z",
             "claimed_expires_at":"2026-02-01T00:00:00Z"}
        ]
        self.candidate={
            "events":self.events,"key_windows":self.windows,
            "attempted_key_ref":self.new,"attempted_at":"2026-01-07T00:00:00Z",
            "transfer_ref":identity.TEST_TRANSFER,
            "challenge_ref":identity.TEST_CHALLENGE
        }

    def eval(self,candidate=None,ctx=None):
        frame={"schema_version":target.SCHEMA,"scope":target.SCOPE,
               "candidate":self.candidate if candidate is None else candidate}
        return target.inspect(anchor.canonical(frame),self.ctx if ctx is None else ctx)

    def denied(self,report):
        self.assertEqual(target.SCOPE,report["scope"])
        for name in target.FLAGS:
            self.assertIs(report[name],False,name)
        for untrusted in (self.old,self.new,"test:time-event-introduced-one",
                          "2026-01-07","Bearer","patient"):
            self.assertNotIn(untrusted,json.dumps(report))

    def test_empty_template_cannot_claim_clock_authority(self):
        raw=(ROOT/"docs/governance/ai-worker-trust-temporal-template.json").read_bytes()
        r=target.inspect(raw,self.ctx)
        self.assertEqual("TRUST_TEMPORAL_CLAIMS_NOT_CONFIGURED",r["status"])
        self.assertTrue(r["synthetic_archive_shape_checked"])
        self.denied(r)

    def test_coherent_comparisons_are_not_authorized(self):
        r=self.eval()
        self.assertEqual("TEMPORAL_CLAIMS_REQUIRE_INDEPENDENT_REVIEW",r["status"])
        self.denied(r)

    def test_end_bound_exclusive_and_no_early_use(self):
        new=self.windows[1]
        r=self.eval({**self.candidate,"attempted_at":new["claimed_expires_at"]})
        self.assertEqual("DENIED_AFTER_CLAIMED_EXPIRY",r["status"])
        self.denied(r)
        later={**new,"claimed_expires_at":"2026-01-06T00:00:00Z"}
        r=self.eval({**self.candidate,"key_windows":[self.windows[0],later]})
        self.assertEqual("DENIED_AFTER_CLAIMED_EXPIRY",r["status"])
        self.denied(r)
        start={**new,"claimed_not_before":"2026-01-09T00:00:00Z"}
        r=self.eval({**self.candidate,"key_windows":[self.windows[0],start]})
        self.assertEqual("DENIED_BEFORE_CLAIMED_VALIDITY",r["status"])
        self.denied(r)

    def test_reversed_or_equal_claimed_interval_denied(self):
        for end in ("2026-01-05T00:00:00Z","2026-01-04T00:00:00Z"):
            changed={**self.windows[1],"claimed_expires_at":end}
            r=self.eval({**self.candidate,"key_windows":[self.windows[0],changed]})
            self.assertEqual("DENIED_INVALID_VALIDITY_ORDER",r["status"])
            self.denied(r)

    def test_missing_duplicate_unknown_validity_window_denied(self):
        for windows,status in (
            ([self.windows[0]],"DENIED_MISSING_OR_UNKNOWN_KEY_WINDOW"),
            (self.windows+[self.windows[1]],"DENIED_DUPLICATE_TIME_WINDOW"),
            ([self.windows[0],{**self.windows[1],"key_ref":"test:unrecognized-key"}],
             "DENIED_MISSING_OR_UNKNOWN_KEY_WINDOW"),
        ):
            r=self.eval({**self.candidate,"key_windows":windows})
            self.assertEqual(status,r["status"])
            self.denied(r)

    def test_regressing_or_tied_observation_time_denied(self):
        for when in ("2026-01-01T00:59:59Z","2026-01-01T01:00:00Z"):
            changed={**self.events[1],"claimed_observed_at":when}
            r=self.eval({**self.candidate,"events":[self.events[0],changed]})
            self.assertEqual("DENIED_EVENT_OBSERVATION_ORDER",r["status"])
            self.denied(r)

    def test_future_effective_event_and_regressing_effective_denied(self):
        for event,status in (
            ({**self.events[1],"claimed_effective_at":"2026-01-08T00:00:00Z"},
             "DENIED_FUTURE_EFFECTIVE_TIME_CLAIM"),
            ({**self.events[1],"claimed_effective_at":"2025-12-31T00:00:00Z"},
             "DENIED_EVENT_EFFECTIVE_TIME_ORDER"),
        ):
            r=self.eval({**self.candidate,"events":[self.events[0],event]})
            self.assertEqual(status,r["status"])
            self.denied(r)

    def test_backdated_revocation_and_retirement_denied(self):
        for kind in ("REVOKED","RETIRED"):
            terminated={"sequence":3,"event_ref":"test:event-termination-three",
                "key_ref":self.new,"kind":kind,"previous_key_ref":None,
                "claimed_effective_at":"2026-01-04T12:00:00Z",
                "claimed_observed_at":"2026-01-08T00:00:00Z"}
            r=self.eval({**self.candidate,"events":self.events+[terminated]})
            self.assertEqual("DENIED_EVENT_EFFECTIVE_TIME_ORDER",r["status"])
            self.denied(r)
            terminated["claimed_effective_at"]="2026-01-05T00:30:00Z"
            r=self.eval({**self.candidate,"events":self.events+[terminated]})
            self.assertEqual("DENIED_BACKDATED_TERMINATION_CLAIM",r["status"])
            self.denied(r)

    def test_revocation_of_current_key_rejects_attempt(self):
        revoked={"sequence":3,"event_ref":"test:event-revoked-three",
            "key_ref":self.new,"kind":"REVOKED","previous_key_ref":None,
            "claimed_effective_at":"2026-01-06T00:00:00Z",
            "claimed_observed_at":"2026-01-06T01:00:00Z"}
        r=self.eval({**self.candidate,"events":self.events+[revoked]})
        self.assertEqual("DENIED_TERMINATED_OR_SUPERSEDED_KEY",r["status"])
        self.denied(r)

    def test_superseded_old_key_rejected_despite_valid_window(self):
        r=self.eval({**self.candidate,"attempted_key_ref":self.old})
        self.assertEqual("DENIED_TERMINATED_OR_SUPERSEDED_KEY",r["status"])
        self.denied(r)

    def test_attempt_before_observed_history_is_denied(self):
        r=self.eval({**self.candidate,"attempted_at":"2026-01-05T00:30:00Z"})
        self.assertEqual("DENIED_ATTEMPT_PRECEDES_OBSERVED_HISTORY",r["status"])
        self.denied(r)

    def test_malformed_utc_timezone_nan_duplicate_and_bounds_denied(self):
        for malformed in ("2026-01-07T00:00:00+00:00","2026-01-07",
             "2026-01-07T00:00:00.123Z","2026-02-30T00:00:00Z",
             "2026-13-01T00:00:00Z",None,True,1,[],
             "2026-01-07T00:00:60Z"):
            r=self.eval({**self.candidate,"attempted_at":malformed})
            self.assertEqual("DENIED_INVALID_TEMPORAL_CLAIM",r["status"],malformed)
            self.denied(r)
        for raw in (b'{"schema_version":1,"schema_version":1}',
                    b'{"schema_version":NaN}',b"\xff",
                    b"x"*(target.MAX_BYTES+1)):
            r=target.inspect(raw,self.ctx)
            self.assertEqual("DENIED_INVALID_TEMPORAL_CLAIM",r["status"])
            self.denied(r)

    def test_attacker_production_clock_signature_flags_denied(self):
        for extra in ("trusted_clock","issuer_signature","approved_by","valid",
                      "revocation_verified","production_ready","key_material"):
            r=self.eval({**self.candidate,extra:"test:forged-stamp"})
            self.assertEqual("DENIED_INVALID_TEMPORAL_CLAIM",r["status"])
            self.denied(r)

    def test_transfer_challenge_replay_and_old_event_sequence(self):
        for name in ("transfer_ref","challenge_ref"):
            r=self.eval({**self.candidate,name:"test:old-transfer-or-nonce"})
            self.assertEqual("DENIED_TEMPORAL_TRANSFER_OR_CHALLENGE_REPLAY",r["status"])
            self.denied(r)
        changed={**self.events[1],"sequence":1}
        r=self.eval({**self.candidate,"events":[self.events[0],changed]})
        self.assertEqual("DENIED_INCONSISTENT_EVENT_LIFECYCLE",r["status"])
        self.denied(r)

    def test_untrusted_prior_context_flags_or_wrong_status_denied(self):
        for fake in (None,{},lifecycle.denied("PRODUCTION_ACTIVE",True),
                     lifecycle.denied("EXTERNAL_TRUST_KEY_LIFECYCLE_NOT_CONFIGURED",False),
                     {**self.ctx,"rotation_approved":True},
                     {**self.ctx,"status":[]},
                     {**self.ctx,"status":True},
                     {**self.ctx,"extra":"spoof"}):
            r=self.eval(ctx=fake)
            self.assertEqual("DENIED_UNTRUSTED_KEY_LIFECYCLE_CONTEXT",r["status"])
            self.assertFalse(r["synthetic_archive_shape_checked"])
            self.denied(r)

if __name__=="__main__":
    unittest.main()
