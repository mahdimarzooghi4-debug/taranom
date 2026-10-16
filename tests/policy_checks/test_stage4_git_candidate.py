"""TRN-TECH-140: Git ancestry evidence must never become a merge or Stage permit."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/"tools/policy_checks"))
import check_stage4_git_candidate as m

class Stage4GitCandidateTests(unittest.TestCase):
    def setUp(self):
        self.doc=m.read_manifest(ROOT/m.MANIFEST)
        self.pinned=self.doc["base_main_sha"]
        self.refs={"refs/remotes/origin/main":self.pinned}
        self.ancestry={}
        prior=self.pinned
        for entry in self.doc["chain"]:
            self.refs["refs/remotes/origin/"+entry["branch"]]=entry["head_sha"]
            self.ancestry[(prior,entry["head_sha"])]=True
            prior=entry["head_sha"]
        self.head="a"*40
        self.refs["HEAD"]=self.head
        self.ancestry[(prior,self.head)]=True

    def assess(self,doc=None,event="push",ref_name=m.TARGET_BRANCH,sha=None):
        def resolver(ref):
            if ref not in self.refs:raise m.InvalidCandidate("REF_MISSING")
            return self.refs[ref]
        return m.assess(doc or self.doc,resolver,
                        lambda a,b:self.ancestry.get((a,b),False),
                        lambda:"b"*40,event=event,ref_name=ref_name,
                        sha=sha or self.head)

    def deny(self,report):
        self.assertEqual("DENIED_UNVERIFIABLE",report["status"])
        self.assertFalse(report["local_git_ancestry_verified"])
        self.assertFalse(report["stage_deployment_authorized"])
        self.assertFalse(report["production_release_allowed"])
        self.assertEqual("DENIED",report["stage4_test_admission"])
        self.assertTrue(report["findings"])

    def test_repo_manifest_complete_and_deny_only(self):
        self.assertEqual((132,133,134,135,136,137,138,139,140,141,143),m.CHAIN)
        self.assertEqual(m.TARGET_BRANCH,self.doc["candidate_branch"])
        self.assertFalse(self.doc["stage4_test_authorized"])
        self.assertFalse(self.doc["production_release_allowed"])

    def test_good_premerge_proof_never_authorizes_stage_or_review(self):
        report=self.assess()
        self.assertEqual("PREMERGE_GIT_ANCESTRY_CONSISTENT",report["status"],report)
        self.assertTrue(report["local_git_ancestry_verified"])
        self.assertFalse(report["github_pr_states_checked_live"])
        self.assertFalse(report["independent_code_review_verified"])
        self.assertEqual("DENIED",report["stage4_test_admission"])
        self.assertEqual(11,len(report["pr_heads"]))

    def test_missing_branch_denies(self):
        self.refs.pop("refs/remotes/origin/"+self.doc["chain"][2]["branch"])
        self.deny(self.assess())

    def test_changed_older_branch_denies(self):
        self.refs["refs/remotes/origin/"+self.doc["chain"][3]["branch"]]="f"*40
        self.deny(self.assess())

    def test_divergent_chain_denies(self):
        parent=self.doc["chain"][4]["head_sha"]
        child=self.doc["chain"][5]["head_sha"]
        self.ancestry.pop((parent,child))
        self.deny(self.assess())

    def test_changed_main_denies(self):
        self.refs["refs/remotes/origin/main"]="f"*40
        self.deny(self.assess())

    def test_wrong_candidate_head_denies(self):
        self.refs["HEAD"]="f"*40
        self.deny(self.assess())

    def test_missing_latest_parent_denies(self):
        self.ancestry.pop((self.doc["chain"][-1]["head_sha"],self.head))
        self.deny(self.assess())

    def test_no_pr_merge_context_spoofing(self):
        self.deny(self.assess(event="pull_request"))
        self.deny(self.assess(ref_name="main"))

    def test_manifest_cannot_self_approve_or_merge(self):
        self.assertFalse(self.doc["chain"][-1]["merged"])
        self.assertFalse(self.doc["chain"][-1]["independently_approved"])
        data=copy.deepcopy(self.doc)
        data["chain"][-1]["merged"]=True
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/"input.json"
            path.write_text(json.dumps(data))
            with self.assertRaisesRegex(m.InvalidCandidate,"PR_CHAIN_ORDER"):
                m.read_manifest(path)

    def test_partial_chain_rejected(self):
        data=copy.deepcopy(self.doc)
        data["chain"].pop()
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/"input.json"
            path.write_text(json.dumps(data))
            with self.assertRaisesRegex(m.InvalidCandidate,"INCOMPLETE_PR_CHAIN"):
                m.read_manifest(path)

    def test_duplicate_json_keys_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/"input.json"
            path.write_text('{"schema_version":1,"schema_version":2}')
            with self.assertRaisesRegex(m.InvalidCandidate,"MANIFEST_JSON_INVALID"):
                m.read_manifest(path)

    def test_git_command_is_read_only_and_no_shell(self):
        import inspect
        source=inspect.getsource(m.git)
        self.assertNotIn("shell=True",source)
        self.assertNotIn("fetch",source)
        self.assertNotIn("push",source)
        self.assertNotIn("merge --",source)

if __name__=="__main__":
    unittest.main()
