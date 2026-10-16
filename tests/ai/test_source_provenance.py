"""TRN-TECH-145: immutable SQLite test source claims/retirement."""
import copy,os,sqlite3,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from ai.taranom_internal.registry import OfflineRegistry,RegistryRejected
from ai.taranom_internal.provenance import register,retire,read_summary,states,NO_PREVIOUS
from ai.taranom_internal.training import fit,compare
from tests.ai.test_shadow_training import sample_manifest,train,bench

def claim(row,tenant="test:team",request=None):
 return {"tenant":tenant,"request_ref":request or "test:claim-"+row["source_ref"][5:],
         "source":copy.deepcopy(row)}
def retiring(ref="test:s1",request="test:retire-s1",tenant="test:team"):
 return {"tenant":tenant,"request_ref":request,"source_ref":ref,"reason_ref":"test:retired-for-fixture"}
class ProvenanceTests(unittest.TestCase):
 def setUp(self):
    self.tmp=tempfile.TemporaryDirectory(prefix="taranom-prov-");self.addCleanup(self.tmp.cleanup)
    self.env=patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":"CI_EPHEMERAL_ONLY"})
    self.env.start();self.addCleanup(self.env.stop)
    self.path=str(Path(self.tmp.name)/"db.sqlite3")
    self.r=OfflineRegistry(self.path);self.r.initialize()
 def claims(self):
    for row in sample_manifest()["sources"]:register(self.r,claim(row))
 def test_registered_pipeline_retains_false_authorization(self):
    self.claims();self.r.freeze_dataset(sample_manifest())
    self.assertEqual("SYNTHETIC_SHADOW_CANDIDATE",fit(self.r,train())["status"])
    self.assertEqual("0",compare(self.r,bench())["shadow_mse"])
    result=self.r.read_inventory("test:team")
    self.assertEqual(3,result["source_provenance"]["recorded_test_source_count"])
    self.assertFalse(result["real_data_eligible"])
    self.assertFalse(result["source_provenance"]["legal_consent_verified"])
 def test_original_untracked_synthetic_flow_stays_nonproduction(self):
    self.r.freeze_dataset(sample_manifest())
    self.assertEqual(0,read_summary(self.r,"test:team")["recorded_test_source_count"])
    self.assertFalse(self.r.read_inventory("test:team")["training_allowed"])
 def test_one_recorded_source_makes_missing_others_deny(self):
    register(self.r,claim(sample_manifest()["sources"][0]))
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_SOURCE_CLAIM_MISSING"):
        self.r.freeze_dataset(sample_manifest())
    with sqlite3.connect(self.path) as conn:
        self.assertEqual(0,conn.execute("SELECT COUNT(*) FROM datasets").fetchone()[0])
 def test_changed_sha_or_consent_declaration_denied(self):
    self.claims()
    for name,value in (("source_sha256","C"*64),("claimed_consent_ref","test:different")):
        v=sample_manifest();v["sources"][0][name]=value
        with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_FROZEN_CLAIM_MISMATCH"):
            self.r.freeze_dataset(v)
 def test_cross_tenant_data_not_authorized_by_other_tenant_events(self):
    self.claims()
    register(self.r,claim(sample_manifest()["sources"][0],tenant="test:other"))
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_SOURCE_CLAIM_MISSING"):
        self.r.freeze_dataset(sample_manifest(tenant="test:other"))
 def test_retired_source_blocks_dataset_freeze_replay(self):
    self.claims();self.r.freeze_dataset(sample_manifest())
    self.assertEqual("TEST_SOURCE_RETIRED",retire(self.r,retiring())["status"])
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_SOURCE_RETIRED"):
        self.r.freeze_dataset(sample_manifest())
 def test_retired_training_source_blocks_fit_and_existing_replay(self):
    self.claims();self.r.freeze_dataset(sample_manifest())
    fit(self.r,train());retire(self.r,retiring("test:s2",request="test:retire-s2"))
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_SOURCE_RETIRED"):
        fit(self.r,train())
 def test_retired_eval_source_blocks_benchmark_and_replay(self):
    self.claims();self.r.freeze_dataset(sample_manifest())
    fit(self.r,train());compare(self.r,bench())
    retire(self.r,retiring("test:s3",request="test:retire-s3"))
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_SOURCE_RETIRED"):
        compare(self.r,bench())
    with sqlite3.connect(self.path) as conn:
        self.assertEqual(1,conn.execute("SELECT COUNT(*) FROM shadow_benchmarks").fetchone()[0])
 def test_same_claim_replay_and_mismatch_denied(self):
    item=sample_manifest()["sources"][0]
    self.assertFalse(register(self.r,claim(item))["rights_verified"])
    self.assertEqual("REPLAY_UNVERIFIED_CLAIM",register(self.r,claim(item))["status"])
    other=copy.deepcopy(item);other["subject_ref"]="test:another"
    with self.assertRaisesRegex(RegistryRejected,"CONFLICTING_SOURCE_CLAIM_REPLAY"):
        register(self.r,claim(other))
 def test_duplicate_source_with_new_request_denied(self):
    item=sample_manifest()["sources"][0]
    register(self.r,claim(item))
    with self.assertRaisesRegex(RegistryRejected,"SOURCE_CLAIM_ALREADY_REGISTERED"):
        register(self.r,claim(item,request="test:another-request"))
 def test_retire_replay_duplicate_or_missing_denied(self):
    with self.assertRaisesRegex(RegistryRejected,"SOURCE_CLAIM_NOT_FOUND"):
        retire(self.r,retiring())
    self.claims();retire(self.r,retiring())
    self.assertEqual("REPLAY_RETIRED_TEST_SOURCE",retire(self.r,retiring())["status"])
    with self.assertRaisesRegex(RegistryRejected,"SOURCE_ALREADY_RETIRED"):
        retire(self.r,retiring(request="test:new-retire"))
 def test_no_authority_fields_or_real_person_refs(self):
    row=sample_manifest()["sources"][0]
    bad=claim(row);bad["source"]["subject_ref"]="person:real"
    with self.assertRaises(RegistryRejected):register(self.r,bad)
    bad=claim(row);bad["source"]["approved"]=True
    with self.assertRaisesRegex(RegistryRejected,"EXACT_METADATA"):
        register(self.r,bad)
    bad=retiring();bad["production"]=True
    with self.assertRaisesRegex(RegistryRejected,"EXACT_METADATA"):
        retire(self.r,bad)
 def test_event_chain_sequence_and_hash(self):
    self.claims();retire(self.r,retiring())
    with self.r.transaction() as conn:record,pos=states(conn,"test:team")
    self.assertEqual(4,pos[0]);self.assertNotEqual(NO_PREVIOUS,pos[1])
    self.assertTrue(record["test:s1"]["retired"])
 def test_append_only_database_denies_update_and_delete(self):
    self.claims()
    with sqlite3.connect(self.path) as con:
        with self.assertRaises(sqlite3.IntegrityError):
            con.execute("UPDATE synthetic_source_events SET event_sha='B'")
        with self.assertRaises(sqlite3.IntegrityError):
            con.execute("DELETE FROM synthetic_source_events")
 def test_attack_corrupt_event_sha_denies_reads_and_training(self):
    self.claims()
    with sqlite3.connect(self.path) as conn:
        conn.execute("DROP TRIGGER source_events_no_update")
        conn.execute("UPDATE synthetic_source_events SET event_sha=? WHERE seq=1",("B"*64,))
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_EVENT_HASH_MISMATCH"):
        read_summary(self.r,"test:team")
    with self.assertRaises(RegistryRejected):
        self.r.freeze_dataset(sample_manifest())
 def test_deleted_middle_event_denies_entire_chain(self):
    self.claims()
    with sqlite3.connect(self.path) as conn:
        conn.execute("DROP TRIGGER source_events_no_delete")
        conn.execute("DELETE FROM synthetic_source_events WHERE seq=2")
    with self.assertRaisesRegex(RegistryRejected,"PROVENANCE_EVENT_CHAIN_BROKEN"):
        read_summary(self.r,"test:team")
 def test_retired_claim_replay_does_not_reopen(self):
    item=sample_manifest()["sources"][0]
    register(self.r,claim(item));retire(self.r,retiring())
    result=register(self.r,claim(item))
    self.assertTrue(result["source_retired"]);self.assertFalse(result["production_allowed"])
 def test_tenant_request_key_isolation(self):
    row=sample_manifest()["sources"][0]
    register(self.r,claim(row));register(self.r,claim(row,tenant="test:other"))
    self.assertEqual(1,read_summary(self.r,"test:other")["recorded_test_source_count"])
if __name__=="__main__":unittest.main()
