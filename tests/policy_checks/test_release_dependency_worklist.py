"""TRN-TECH-164: only vetted repository sources create a denial-only worklist."""
import copy
import csv
import io
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools/policy_checks"))
import build_release_dependency_worklist as work
import check_epic_capability_progress as epic

class DependencyWorklistTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        for filename in (epic.REGISTRY,epic.BLOCKERS):
            target=self.root/filename
            target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(ROOT/filename,target)
        shutil.copytree(ROOT/"docs/decisions",self.root/"docs/decisions")
        self.cap=json.loads((self.root/epic.REGISTRY).read_text())
        self.block=json.loads((self.root/epic.BLOCKERS).read_text())
        for c in self.cap["capabilities"]:
            for k in ("implementation_ref","test_ref"):
                if c[k]:
                    p=self.root/c[k]
                    p.parent.mkdir(parents=True,exist_ok=True)
                    p.write_text("synthetic reference fixture",encoding="utf-8")

    def save(self):
        (self.root/epic.REGISTRY).write_text(json.dumps(self.cap))
        (self.root/epic.BLOCKERS).write_text(json.dumps(self.block))

    def assert_denial(self):
        self.save()
        result=work.inspect(self.root)
        self.assertEqual(work.DENIED,result["status"],result)
        self.assertEqual([],result["work_items"])
        self.assertFalse(result["production_ready"])
        self.assertFalse(result["national_release_allowed"])
        self.assertFalse(result["formal_approval_observed"])

    def test_current_real_repo_has_traceable_denial_only_17_by_18(self):
        result=work.inspect(ROOT)
        self.assertEqual(work.STATUS,result["status"])
        self.assertEqual(17,result["blocked_domains"])
        self.assertEqual(18,len(result["work_items"]))
        self.assertEqual(39,result["candidate_capabilities"])
        self.assertEqual(18,result["required_owners_unassigned"])
        self.assertEqual({"TRN-EP-"+str(i).zfill(2) for i in range(1,14)}|
                         {"MARKET-PUBLIC","MARKET-SUPPORT","MARKET-CORPORATE",
                          "NATIONAL-COVERAGE"},
                         {x for i in result["work_items"] for x in i["blocked_domain_ids"]})
        for i in result["work_items"]:
            self.assertEqual("NOT_ASSIGNED",i["decision_owner"])
            self.assertEqual("BLOCKED_UNVERIFIED",i["evidence_status"])
            self.assertEqual("NOT_OBSERVED",i["human_decision_status"])
            self.assertIs(i["formal_acceptance_observed"],False)
            self.assertEqual(sorted(i["policy_refs"]),i["policy_refs"])
        self.assertFalse(result["national_release_allowed"])
        self.assertFalse(result["formal_approval_observed"])
        self.assertEqual("DENIED_BY_DEFAULT",result["release_admission"])

    def test_valid_synthetic_copy_and_policy_links(self):
        self.save()
        r=work.inspect(self.root)
        self.assertEqual(work.STATUS,r["status"])
        self.assertTrue(all(p.startswith("TRN-BIZ-")
                            for i in r["work_items"] for p in i["policy_refs"]))
        self.assertEqual(64,len(r["source_fingerprints_sha256"][work.BLOCKER_ROOT]))
        m=work.markdown_report(r)
        self.assertIn("DENIED_BY_DEFAULT",m)
        self.assertIn("TRN-EP-11",m)
        self.assertNotIn("READY_FOR_PRODUCTION",m)
        rows=list(csv.DictReader(io.StringIO(work.csv_report(r))))
        self.assertEqual(18,len(rows))
        self.assertTrue(all(x["national_release_allowed"]=="false" for x in rows))
        self.assertNotIn("secret",m.lower())

    def test_forged_release_approval_denied(self):
        self.block["release_admission"]="READY"
        self.assert_denial()

    def test_changed_national_scope_or_market_missing_denied(self):
        self.block["items"]=[x for x in self.block["items"] if x["id"]!="MARKET-PUBLIC"]
        self.assert_denial()

    def test_removed_evidence_category_denied(self):
        item=self.block["items"][0]
        item["missing_evidence"]=[x for x in item["missing_evidence"]
                                  if x!="LEGAL_OPERATOR_IDENTITY"]
        self.assert_denial()

    def test_duplicate_capability_and_fake_acceptance_denied(self):
        self.cap["capabilities"][1]=copy.deepcopy(self.cap["capabilities"][0])
        self.assert_denial()
        self.cap=json.loads((ROOT/epic.REGISTRY).read_text())
        self.cap["capabilities"][0]["formal_acceptance_observed"]=True
        self.assert_denial()

    def test_deleted_code_link_does_not_count_as_ready(self):
        p=next(c for c in self.cap["capabilities"] if c["implementation_ref"])
        (self.root/p["implementation_ref"]).unlink()
        self.assert_denial()

    def test_symlinked_source_and_malformed_json_denied(self):
        p=self.root/epic.BLOCKERS
        p.unlink()
        p.symlink_to(ROOT/epic.BLOCKERS)
        self.assertEqual(work.DENIED,work.inspect(self.root)["status"])
        p.unlink()
        p.write_bytes(b'{"schema_version":1,"schema_version":2}')
        self.assertEqual(work.DENIED,work.inspect(self.root)["status"])

    def test_no_clobber_and_symlink_reports(self):
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/"report.md"
            work.write_new(path,"DENIED_BY_DEFAULT\n")
            with self.assertRaises(ValueError):
                work.write_new(path,"READY")
            target=Path(d)/"target"
            target.write_text("unchanged")
            link=Path(d)/"alias"
            link.symlink_to(target)
            with self.assertRaises(ValueError):
                work.write_new(link,"READY")
            self.assertEqual("unchanged",target.read_text())

if __name__=="__main__":
    unittest.main()
