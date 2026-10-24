"""TRN-TECH-138: stage4 synthetic rehearsal source, never admission."""
import copy
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from tools.policy_checks.check_stage4_test_preflight import (
    CONTRACT, GATES, MANIFEST, REQUIRED_PR, inspect, main,
)

ROOT = Path(__file__).resolve().parents[2]

class Stage4TestPreflightTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="trn-stage4-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        stage = self.root / MANIFEST
        stage.parent.mkdir(parents=True)
        self.base = json.loads((ROOT / MANIFEST).read_text("utf-8"))
        national = self.root / "docs/governance/national-launch-blockers.json"
        shutil.copyfile(ROOT / "docs/governance/national-launch-blockers.json", national)
        for src in (ROOT / "docs/decisions").glob("TRN-BIZ-*.md"):
            dst = self.root / "docs/decisions" / src.name
            dst.parent.mkdir(parents=True, exist_ok=True)
            dst.write_text("Synthetic file for source validation only", encoding="utf-8")

    def check(self, change=None):
        data = copy.deepcopy(self.base)
        if change: change(data)
        (self.root / MANIFEST).write_text(json.dumps(data), encoding="utf-8")
        return inspect(self.root)

    def denied(self, edit):
        report = self.check(edit)
        self.assertFalse(report["source_snapshot_consistent"], report)
        self.assertEqual("DENIED", report["stage4_test_admission"])
        self.assertFalse(report["stage_deployment_allowed"])
        self.assertFalse(report["production_release_allowed"])
        self.assertTrue(report["findings"])

    def test_real_source_reports_only_denial(self):
        report = inspect(ROOT)
        self.assertTrue(report["source_snapshot_consistent"], report)
        self.assertEqual("DENIED", report["stage4_test_admission"])
        self.assertTrue(report["synthetic_ci_is_not_stage"])
        self.assertFalse(report["live_pr_checks_performed"])
        self.assertEqual(17, report["national_unverified_dimensions"])
        self.assertEqual(list(range(132,141)), report["candidate_prs_from_snapshot"])

    def test_even_valid_source_never_admits_stage(self):
        report = self.check()
        self.assertTrue(report["source_snapshot_consistent"], report)
        self.assertFalse(report["stage_deployment_allowed"])
        self.assertFalse(report["independent_review_verified"])
        self.assertFalse(report["stage4_target_verified"])

    def test_no_self_granted_stage_testing(self):
        self.denied(lambda x: x.update(stage_test_admission="READY"))
    def test_no_fabricated_stage_target(self):
        self.denied(lambda x: x.update(environment="stage4.example"))
    def test_no_stage_deployment(self):
        self.denied(lambda x: x.update(stage_deployment_authorized=True))
    def test_no_production_promotion(self):
        self.denied(lambda x: x.update(production_release_allowed=True))
    def test_no_actual_people(self):
        self.denied(lambda x: x.update(real_user_access_allowed=True))
    def test_no_partner_assertions(self):
        self.denied(lambda x: x.update(external_partner_access_verified=True))
    def test_cannot_self_sign_independent_review(self):
        self.denied(lambda x: x.update(reviewers_independently_approved=True))
    def test_no_hidden_tenant_data(self):
        self.denied(lambda x: x.update(scope="PRODUCTION_ORGANIZATION"))
    def test_pr_stack_not_partial(self):
        self.denied(lambda x: x["github_snapshot"].update(pr_numbers=[132,140]))
    def test_pr_stack_cannot_claim_merge(self):
        self.denied(lambda x: x["github_snapshot"].update(chain_is_merged=True))
    def test_static_snapshot_not_fake_live(self):
        self.denied(lambda x: x["github_snapshot"].update(is_live_verification=True))
    def test_explicit_stage_gates_cannot_disappear(self):
        self.denied(lambda x: x.update(pending_stage_gates=list(GATES)[:-1]))
    def test_no_extra_fields_claiming_approval(self):
        self.denied(lambda x: x.update(stage4_approved=True))
    def test_national_source_cannot_be_replaced(self):
        self.denied(lambda x: x.update(national_blockers_manifest="mock.json"))
    def test_explicit_contract(self):
        self.denied(lambda x: x.update(contract="TRN-TECH-FAKE"))

    def test_duplicate_keys_denied(self):
        (self.root / MANIFEST).write_text('{"schema_version":1,"schema_version":2}')
        self.assertFalse(inspect(self.root)["source_snapshot_consistent"])

    def test_missing_manifest_denied(self):
        self.assertFalse(inspect(self.root)["source_snapshot_consistent"])

    def test_symlinked_manifest_denied(self):
        p = self.root / MANIFEST
        p.symlink_to(ROOT / MANIFEST)
        self.assertFalse(inspect(self.root)["source_snapshot_consistent"])

    def test_national_blocker_missing_denied(self):
        self.check()
        (self.root / "docs/governance/national-launch-blockers.json").unlink()
        self.assertFalse(inspect(self.root)["source_snapshot_consistent"])

    def test_national_ready_claim_denied(self):
        self.check()
        p = self.root / "docs/governance/national-launch-blockers.json"
        data=json.loads(p.read_text())
        data["release_admission"]="READY"
        p.write_text(json.dumps(data))
        self.assertFalse(inspect(self.root)["source_snapshot_consistent"])

    def test_cli_report_is_denied_even_when_source_consistent(self):
        self.check()
        out=self.root/"result.json"
        self.assertEqual(0,main(["--root",str(self.root),"--report-json",str(out)]))
        data=json.loads(out.read_text())
        self.assertFalse(data["production_release_allowed"])
        self.assertEqual("DENIED",data["stage4_test_admission"])
        self.assertFalse(data["live_pr_checks_performed"])

    def test_cli_returns_failure_on_forged_status(self):
        self.check(lambda x:x.update(stage_deployment_authorized=True))
        output=self.root/"result.json"
        self.assertEqual(1,main(["--root",str(self.root),"--report-json",str(output)]))
        self.assertFalse(json.loads(output.read_text())["source_snapshot_consistent"])

if __name__ == "__main__":
    unittest.main()
