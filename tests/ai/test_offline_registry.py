"""Executable internal AI offline registry, real isolated SQLite regression."""
import copy
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from ai.taranom_internal.registry import OfflineRegistry,RegistryRejected

SHA="A"*64
SRC="B"*64
def dataset(tenant="test:tenant-a",version="test:dataset-v1",request="test:freeze-1"):
    return {"tenant":tenant,"version_ref":version,"purpose_ref":"test:research",
            "category_ref":"test:metadata","request_ref":request,"sources":[
       {"source_ref":"test:source-1","subject_ref":"test:member-1",
        "partition":"TRAINING","source_sha256":SRC,
        "claimed_consent_ref":"test:unverified-consent-1",
        "claimed_rights_review_ref":"test:unverified-rights-1"},
       {"source_ref":"test:source-2","subject_ref":"test:member-2",
        "partition":"INDEPENDENT_EVALUATION","source_sha256":SRC,
        "claimed_consent_ref":"test:unverified-consent-2",
        "claimed_rights_review_ref":"test:unverified-rights-2"}]}
def model(tenant="test:tenant-a",request="test:register-1"):
    return {"tenant":tenant,"model_ref":"test:synthetic-model",
            "version_ref":"test:model-v1","dataset_ref":"test:dataset-v1",
            "adapter_ref":"test:unimplemented-adapter",
            "artifact_sha256":SHA,"request_ref":request}
def evaluation(tenant="test:tenant-a",request="test:evaluate-1"):
    return {"tenant":tenant,"evaluation_ref":"test:eval-v1",
            "model_ref":"test:synthetic-model","version_ref":"test:model-v1",
            "dataset_ref":"test:dataset-v1","request_ref":request,
            "samples":[{"source_ref":"test:source-2","observed":"2.0","predicted":"1.5"}]}

class OfflineAiRegistryTests(unittest.TestCase):
    def setUp(self):
        self.folder=tempfile.TemporaryDirectory(prefix="taranom-ai-ci-")
        self.addCleanup(self.folder.cleanup)
        self.db=str(Path(self.folder.name)/"registry.sqlite3")
        self.env=patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":"CI_EPHEMERAL_ONLY"})
        self.env.start();self.addCleanup(self.env.stop)
        self.r=OfflineRegistry(self.db)
        self.r.initialize()
    def filled(self):
        self.r.freeze_dataset(dataset())
        self.r.register_model(model())
    def test_persistent_registry_full_offline_flow(self):
        self.filled()
        a=self.r.evaluate_synthetic(evaluation())
        self.assertEqual("MEASURED_SYNTHETIC_ONLY",a["status"])
        self.assertEqual("0.25",a["mse"])
        self.assertEqual("0.5",a["mae"])
        other=OfflineRegistry(self.db).read_inventory("test:tenant-a")
        self.assertEqual(1,len(other["datasets"]))
        self.assertEqual(1,len(other["model_versions"]))
        self.assertEqual(1,len(other["evaluations"]))
        self.assertFalse(other["production_allowed"])
        self.assertFalse(other["training_allowed"])
    def test_no_test_flag_blocks_every_access(self):
        with patch.dict(os.environ,{"TARANOM_AI_OFFLINE_TEST_ONLY":"false"}):
            with self.assertRaisesRegex(RegistryRejected,"EPHEMERAL"):
                self.r.read_inventory("test:tenant-a")
    def test_no_relative_or_out_of_temp_db(self):
        for path in ("registry.sqlite3","/var/lib/taranom/ai.sqlite3"):
            with self.assertRaises(RegistryRejected):
                OfflineRegistry(path).initialize()
    def test_immutable_versions_no_overwrite(self):
        self.filled()
        with sqlite3.connect(self.db) as c:
            with self.assertRaises(sqlite3.IntegrityError):
                c.execute("UPDATE model_versions SET adapter_ref='test:evil'")
            with self.assertRaises(sqlite3.IntegrityError):
                c.execute("DELETE FROM datasets")
    def test_order_invariant_manifest_and_exact_replay(self):
        d=dataset()
        a=self.r.freeze_dataset(d)
        d["sources"].reverse()
        b=self.r.freeze_dataset(d)
        self.assertEqual(a["manifest_sha256"],b["manifest_sha256"])
        self.assertEqual("REPLAY_UNVERIFIED",b["status"])
    def test_changed_dataset_replay_denied(self):
        self.r.freeze_dataset(dataset())
        d=dataset();d["sources"][0]["source_sha256"]="C"*64
        with self.assertRaisesRegex(RegistryRejected,"CONFLICTING"):
            self.r.freeze_dataset(d)
    def test_dataset_version_cannot_mutate_through_new_request(self):
        self.r.freeze_dataset(dataset())
        with self.assertRaisesRegex(RegistryRejected,"ALREADY_FROZEN"):
            self.r.freeze_dataset(dataset(request="test:other-request"))
    def test_source_overlap_across_partitions_rejected(self):
        d=dataset()
        d["sources"][1]["source_ref"]=d["sources"][0]["source_ref"]
        with self.assertRaisesRegex(RegistryRejected,"SOURCE_OVERLAP"):
            self.r.freeze_dataset(d)
    def test_subject_leakage_across_partitions_rejected(self):
        d=dataset();d["sources"][1]["subject_ref"]="test:member-1"
        with self.assertRaisesRegex(RegistryRejected,"SUBJECT_OVERLAP"):
            self.r.freeze_dataset(d)
    def test_cannot_use_only_training_partition(self):
        d=dataset();d["sources"][1]["partition"]="TRAINING"
        with self.assertRaisesRegex(RegistryRejected,"BOTH_PARTITIONS"):
            self.r.freeze_dataset(d)
    def test_missing_rights_ref_denied(self):
        d=dataset();d["sources"][0]["claimed_rights_review_ref"]=None
        with self.assertRaises(RegistryRejected):self.r.freeze_dataset(d)
    def test_cannot_provide_real_member_identifiers(self):
        d=dataset();d["sources"][0]["subject_ref"]="person:123"
        with self.assertRaises(RegistryRejected):self.r.freeze_dataset(d)
    def test_reject_unknown_claim_flags(self):
        d=dataset();d["authorized"]=True
        with self.assertRaisesRegex(RegistryRejected,"EXACT"):
            self.r.freeze_dataset(d)
    def test_model_requires_frozen_same_tenant_dataset(self):
        with self.assertRaisesRegex(RegistryRejected,"DATASET_NOT_FOUND"):
            self.r.register_model(model())
        self.r.freeze_dataset(dataset())
        with self.assertRaisesRegex(RegistryRejected,"DATASET_NOT_FOUND"):
            self.r.register_model(model(tenant="test:other"))
    def test_same_model_replay_and_conflicting_request(self):
        self.filled()
        self.assertEqual("REPLAY_UNVERIFIED",self.r.register_model(model())["status"])
        m=model();m["artifact_sha256"]="C"*64
        with self.assertRaisesRegex(RegistryRejected,"CONFLICTING"):
            self.r.register_model(m)
    def test_model_revision_duplicate_denied(self):
        self.filled()
        with self.assertRaisesRegex(RegistryRejected,"ALREADY_REGISTERED"):
            self.r.register_model(model(request="test:new-request"))
    def test_evaluation_exact_replay_and_conflict(self):
        self.filled()
        self.r.evaluate_synthetic(evaluation())
        self.assertEqual("REPLAY_SYNTHETIC_ONLY",
                         self.r.evaluate_synthetic(evaluation())["status"])
        x=evaluation();x["samples"][0]["predicted"]="1.2"
        with self.assertRaisesRegex(RegistryRejected,"CONFLICTING"):
            self.r.evaluate_synthetic(x)
    def test_training_sample_cannot_enter_evaluation(self):
        self.filled()
        e=evaluation();e["samples"][0]["source_ref"]="test:source-1"
        with self.assertRaisesRegex(RegistryRejected,"INDEPENDENT"):
            self.r.evaluate_synthetic(e)
    def test_wrong_tenant_and_wrong_model_dataset_denied(self):
        self.filled()
        with self.assertRaisesRegex(RegistryRejected,"LINEAGE"):
            self.r.evaluate_synthetic(evaluation(tenant="test:other"))
    def test_evaluation_rejects_nan_infinity_or_numeric_native(self):
        self.filled()
        for val in ("NaN","Infinity","1e999999",3.14,"1000000001"):
            e=evaluation();e["samples"][0]["predicted"]=val
            with self.assertRaises(RegistryRejected):self.r.evaluate_synthetic(e)
    def test_dataset_and_model_has_no_activation_commands(self):
        self.filled()
        inventory=self.r.read_inventory("test:tenant-a")
        self.assertFalse(inventory["real_data_eligible"])
        self.assertFalse(inventory["model_versions"][0]["inference_supported"])
        self.assertEqual("DENIED",inventory["stage_admission"])
    def test_no_cross_tenant_inventory_leak(self):
        self.filled()
        blank=self.r.read_inventory("test:other")
        self.assertEqual([],blank["datasets"])
        self.assertEqual([],blank["model_versions"])
        self.assertEqual([],blank["evaluations"])
    def test_cli_persistent_no_network(self):
        env={**os.environ,"TARANOM_AI_OFFLINE_TEST_ONLY":"CI_EPHEMERAL_ONLY"}
        cmd=[sys.executable,"-m","ai.taranom_internal.cli","--db",self.db]
        p=subprocess.run(cmd+["inventory","--tenant","test:tenant-a"],
                         text=True,capture_output=True,env=env,check=False)
        self.assertEqual(0,p.returncode,p.stderr)
        self.assertFalse(json.loads(p.stdout)["production_allowed"])
    def test_cli_file_rejects_unknown_authority(self):
        file=Path(self.folder.name)/"claims.json"
        d=dataset();d["authorizeProduction"]=True
        file.write_text(json.dumps(d),encoding="utf8")
        cmd=[sys.executable,"-m","ai.taranom_internal.cli","--db",self.db,
             "freeze-dataset","--input-json",str(file)]
        p=subprocess.run(cmd,text=True,capture_output=True,check=False)
        self.assertEqual(2,p.returncode)
        self.assertEqual("DENIED",json.loads(p.stdout)["status"])
    def test_technical_policy_retains_no_external_ai_dependency(self):
        import inspect
        import ai.taranom_internal.registry as reg
        source=inspect.getsource(reg)
        self.assertNotIn("requests.",source)
        self.assertNotIn("http.client",source)
        self.assertNotIn("promote_model",source)
        self.assertNotIn("activate_production",source)

if __name__=="__main__":unittest.main()
