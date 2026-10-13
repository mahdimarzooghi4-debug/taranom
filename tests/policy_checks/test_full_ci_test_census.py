"""TRN-TECH-101: fail-closed test inventory vs actual full CI commands."""
from pathlib import Path
import json
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/policy_checks"))
from check_full_ci_test_census import inspect, main, WORKFLOW, GROUPS


class FullCiTestCensusTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.workflow = self.root / WORKFLOW
        self.workflow.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / WORKFLOW, self.workflow)
        self.testfiles = {}
        for kind, (folder, _) in GROUPS.items():
            directory = self.root / folder
            directory.mkdir(parents=True)
            names = [p.name for p in (ROOT / folder).glob("test_*.py")]
            self.assertTrue(names)
            self.testfiles[kind] = sorted(names)
            for name in names:
                (directory / name).write_text("# local filename fixture\n")

    def replace(self, old, new, count=1):
        source = self.workflow.read_text()
        self.assertGreaterEqual(source.count(old), count)
        self.workflow.write_text(source.replace(old, new, count))

    def denied(self, rule):
        out = inspect(self.root)
        self.assertEqual("BLOCKED", out["status"], out)
        self.assertIn(rule, out["findings"])
        self.assertFalse(out["release_allowed"])
        self.assertFalse(out["security_clearance"])
        self.assertFalse(out["runtime_coverage_certified"])

    def test_complete_real_test_corpus_is_executed_once(self):
        report = inspect(self.root)
        self.assertEqual("CONSISTENT_SOURCE_ONLY", report["status"], report)
        for kind, names in self.testfiles.items():
            record = report["counts"][kind]
            self.assertEqual(len(names), record["tracked_test_files"])
            self.assertEqual(len(names), record["literal_invocations"])
            self.assertEqual(0, record["not_invoked"])
            self.assertEqual(0, record["duplicate_invocations"])
        self.assertFalse(report["release_allowed"])

    def test_regression_file_added_but_not_called_is_denied(self):
        (self.root / "tests/center_demo/test_new_workflow.py").write_text("pass\n")
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_new_ops_http_test_silently_skipped_is_denied(self):
        (self.root / "tests/ops_status/test_provider_claim.py").write_text("pass\n")
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_new_security_test_silently_skipped_is_denied(self):
        (self.root / "tests/policy_checks/test_release_integrity.py").write_text("pass\n")
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_removing_center_command_denied(self):
        self.replace("          python3 tests/center_demo/test_support_history_http.py\n", "")
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_removing_mainline_ci_security_unit_test_denied(self):
        self.replace("          python3 -m unittest discover -s tests/policy_checks -p 'test_mainline_full_ci.py' -v\n", "")
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_duplicate_center_execution_denied(self):
        line = "          python3 tests/center_demo/test_cost_journal_http.py\n"
        self.replace(line, line + line)
        self.denied("DUPLICATE_TEST_EXECUTION")

    def test_removed_test_file_still_in_workflow_denied(self):
        (self.root / "tests/ops_status/test_local_ops_http.py").unlink()
        self.denied("WORKFLOW_INVOCATION_HAS_NO_TEST_FILE")

    def test_renaming_only_test_file_denied(self):
        p = self.root / "tests/center_demo/test_cost_history_http.py"
        p.rename(p.with_name("test_cost_history_renamed.py"))
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_script_hidden_behind_shell_or_true_is_denied(self):
        old = "python3 tests/ops_status/test_payer_scenario_http.py"
        self.replace(old, old + " || true")
        self.denied("NONCANONICAL_TEST_INVOCATION")

    def test_policy_test_in_comment_does_not_count(self):
        line = "          python3 -m unittest discover -s tests/policy_checks -p 'test_public_source_history.py' -v"
        self.replace(line, "          # " + line.strip())
        self.denied("COMMITTED_TEST_NOT_EXECUTED")

    def test_echo_instead_of_running_is_denied(self):
        line = "python3 tests/center_demo/test_cost_support_handoff_http.py"
        self.replace(line, "echo '" + line + "'")
        self.denied("NONCANONICAL_TEST_INVOCATION")

    def test_moving_test_to_wrong_job_denied(self):
        old = "          python3 tests/center_demo/test_real_center_demo_http.py"
        # Place a standalone policy test in the wrong PostgreSQL runner.
        self.replace(old,
                     old + "\n          python3 tests/policy_checks/test_local_portal_csp.py")
        self.denied("TEST_IN_WRONG_CI_JOB")

    def test_browser_full_suite_filtering_denied(self):
        self.replace("          CI=true npm run test:e2e",
                     "          CI=true npm run test:e2e -- --grep smoke")
        self.denied("FULL_BROWSER_TEST_INVOCATION_MISSING")

    def test_symlinked_test_file_is_denied(self):
        file = self.root / "tests/center_demo/test_support_history_http.py"
        file.unlink()
        file.symlink_to(self.root / "tests/center_demo/test_cost_journal_http.py")
        self.denied("UNSAFE_TEST_FILE")

    def test_missing_canonical_workflow_denied(self):
        self.workflow.unlink()
        self.denied("CANONICAL_WORKFLOW_UNAVAILABLE")

    def test_duplicate_job_heading_denied(self):
        with self.workflow.open("a") as stream:
            stream.write("\n  real-local-ops-http-react:\n    runs-on: ubuntu-latest\n")
        self.denied("REQUIRED_TEST_JOB_MISSING_OR_DUPLICATE")

    def test_redacted_cli_output_does_not_claim_approval(self):
        report_file = self.root / "result.json"
        with patch.object(sys, "argv", ["census", "--root", str(self.root),
                                        "--report-json", str(report_file)]):
            self.assertEqual(0, main())
        data = json.loads(report_file.read_text())
        self.assertEqual("CONSISTENT_SOURCE_ONLY", data["status"])
        self.assertFalse(data["release_allowed"])
        self.assertFalse(data["runtime_coverage_certified"])
        self.assertEqual([], data["findings"])

    def test_real_checked_in_workflow_covers_every_python_test(self):
        report = inspect(ROOT)
        self.assertEqual("CONSISTENT_SOURCE_ONLY", report["status"], report)
        self.assertEqual(3, len(report["counts"]))


if __name__ == "__main__":
    unittest.main()
