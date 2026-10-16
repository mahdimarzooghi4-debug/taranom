"""Live synthetic SQLite learner and independent partition tests."""
import copy
import json
import os
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from ai.taranom_internal.registry import OfflineRegistry,RegistryRejected
from ai.taranom_internal.training import fit,compare,ALGORITHM

SHA="A"*64
def sample_manifest(tenant="test:team"):
    return {"tenant":tenant,"version_ref":"test:ds","purpose_ref":"test:research",
       "category_ref":"test:synthetic","request_ref":"test:freeze",
       "sources":[
        {"source_ref":s,"subject_ref":u,"partition":p,"source_sha256":SHA,
         "claimed_consent_ref":"test:unverified","claimed_rights_review_ref":"test:unverified"}
        for s,u,p in [
         ("test:s1","test:u1","TRAINING"),
         ("test:s2","test:u2","TRAINING"),
         ("test:s3","test:u3","INDEPENDENT_EVALUATION")]]}
def train(tenant="test:team"):
    return {"tenant":tenant,"training_ref":"test:train","model_ref":"test:model",
      "version_ref":"test:shadow-v1","dataset_ref":"test:ds",
      "request_ref":"test:fit-once","samples":[
       {"source_ref":"test:s1","feature":"0","observed":"1"},
       {"source_ref":"test:s2","feature":"1","observed":"3"}]}
def bench(tenant="test:team"):
    return {"tenant":tenant,"benchmark_ref":"test:bench","training_ref":"test:train",
            "request_ref":"test:bench-once","samples":[
             {"source_ref":"test:s3","feature":"2","observed":"5"}]}
class ShadowLearnerTests(unittest.TestCase):
 def setUp(self):
    self.tmp=tempfile.TemporaryDirectory(prefix="taranom-shadow-");self.addCleanup(self.tmp.cleanup)
    self.env=patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":"CI_EPHEMERAL_ONLY"})
    self.env.start();self.addCleanup(self.env.stop)
    self.path=str(Path(self.tmp.name)/"db.sqlite3")
    self.r=OfflineRegistry(self.path);self.r.initialize()
 def dataset(self):self.r.freeze_dataset(sample_manifest())
 def trained(self):self.dataset();return fit(self.r,train())
 def test_fit_and_independent_benchmark_real_sqlite(self):
    output=self.trained()
    self.assertEqual("SYNTHETIC_SHADOW_CANDIDATE",output["status"])
    self.assertFalse(output["production_allowed"])
    result=compare(self.r,bench())
    self.assertEqual(("9","0","3","0"),(result["baseline_mse"],result["shadow_mse"],
                                           result["baseline_mae"],result["shadow_mae"]))
    self.assertFalse(result["winner_selected"])
    rows=OfflineRegistry(self.path).read_inventory("test:team")
    self.assertEqual(1,len(rows["training_runs"]))
    self.assertEqual(1,len(rows["shadow_benchmarks"]))
    self.assertEqual("UNVERIFIED_OFFLINE",rows["model_versions"][0]["status"])
    self.assertFalse(rows["production_allowed"])
 def test_training_replay_same_sha(self):
    original=self.trained()
    replay=fit(self.r,train())
    self.assertEqual("REPLAY_SHADOW_ONLY",replay["status"])
    self.assertEqual(original["artifact_sha256"],replay["artifact_sha256"])
 def test_training_changed_replay_denied(self):
    self.trained();other=train();other["samples"][0]["observed"]="9"
    with self.assertRaisesRegex(RegistryRejected,"CONFLICTING"):
        fit(self.r,other)
 def test_training_must_use_only_exact_manifest_sources(self):
    self.dataset();x=train();x["samples"][1]["source_ref"]="test:s3"
    with self.assertRaisesRegex(RegistryRejected,"TRAINING_PARTITION"):
        fit(self.r,x)
 def test_training_requires_positive_variance(self):
    self.dataset();x=train();x["samples"][1]["feature"]="0"
    with self.assertRaisesRegex(RegistryRejected,"DEGENERATE"):
        fit(self.r,x)
 def test_no_tenant_crossing(self):
    self.dataset()
    with self.assertRaisesRegex(RegistryRejected,"DATASET_NOT_FOUND"):
        fit(self.r,train(tenant="test:another"))
 def test_training_fails_closed_without_opt_in(self):
    self.dataset()
    with patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":"no"}):
        with self.assertRaises(RegistryRejected):fit(self.r,train())
 def test_benchmark_requires_training_run(self):
    self.dataset()
    with self.assertRaisesRegex(RegistryRejected,"TRAINING_NOT_FOUND"):
        compare(self.r,bench())
 def test_benchmark_training_leak_denied(self):
    self.trained();x=bench();x["samples"][0]["source_ref"]="test:s1"
    with self.assertRaisesRegex(RegistryRejected,"HELD_OUT"):
        compare(self.r,x)
 def test_benchmark_tamper_replay_denied(self):
    self.trained();compare(self.r,bench())
    x=bench();x["samples"][0]["observed"]="6"
    with self.assertRaisesRegex(RegistryRejected,"CONFLICTING"):
        compare(self.r,x)
 def test_model_artifact_sha_matches_stored_artifact(self):
    self.trained()
    with sqlite3.connect(self.path) as c:
        run=c.execute("SELECT artifact_sha,artifact_json FROM shadow_training_runs").fetchone()
    from ai.taranom_internal.registry import _hash
    self.assertEqual(_hash(json.loads(run[1])),run[0])
    self.assertEqual(ALGORITHM,json.loads(run[1])["algorithm"])
 def test_db_cannot_modify_training_or_benchmark(self):
    self.trained();compare(self.r,bench())
    with sqlite3.connect(self.path) as c:
        for table in ("shadow_training_runs","shadow_benchmarks"):
            with self.assertRaises(sqlite3.IntegrityError):
                c.execute(f"DELETE FROM {table}")
 def test_model_version_conflict_denied(self):
    self.trained()
    x=train();x["request_ref"]="test:another";x["training_ref"]="test:another"
    with self.assertRaises(RegistryRejected):fit(self.r,x)
 def test_input_nan_and_unbounded_denied(self):
    self.dataset()
    for val in ("NaN","Infinity","1e9999","1000000001",1.0):
        x=train();x["samples"][0]["observed"]=val
        with self.assertRaises(RegistryRejected):fit(self.r,x)
 def test_evaluation_math_does_not_choose_winner(self):
    self.trained();result=compare(self.r,bench())
    self.assertFalse(result["human_approved"])
    self.assertFalse(result["clinical_evaluation"])
    self.assertFalse(result["threshold_defined"])
    self.assertFalse(result["production_allowed"])
 def test_exact_replay_benchmark(self):
    self.trained();compare(self.r,bench())
    self.assertEqual("REPLAY_SHADOW_DIAGNOSTIC",compare(self.r,bench())["status"])
 def test_missing_human_authority_rejected_by_schema(self):
    self.dataset();x=train();x["approveProduction"]=True
    with self.assertRaisesRegex(RegistryRejected,"EXACT_METADATA_SCHEMA"):
        fit(self.r,x)

if __name__=="__main__":unittest.main()
