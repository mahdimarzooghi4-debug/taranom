"""TRN-TECH-080: genuine canonical workflow + hostile reusable-call fixtures."""
import shutil
import tempfile
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/policy_checks"))
from check_public_ci_workflow_trust import (
    scan, inspect_workflow, REUSABLE_NAME, DISPATCHER_NAME, REQUIRED_FULL_JOBS
)


class SharedFullCiTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.workflows = self.root / ".github/workflows"
        self.workflows.mkdir(parents=True)
        for name in (REUSABLE_NAME, DISPATCHER_NAME):
            shutil.copy2(ROOT / ".github/workflows" / name, self.workflows / name)

    def assert_denied(self, rule):
        result = scan(self.root)
        self.assertEqual(result["status"], "BLOCKED")
        self.assertIn(rule, {i["rule"] for i in result["findings"]})
        self.assertFalse(result["release_allowed"])
        self.assertFalse(result["security_clearance"])

    def edit(self, name, old, new):
        path = self.workflows / name
        src = path.read_text()
        self.assertIn(old, src)
        path.write_text(src.replace(old, new, 1))

    def test_real_canonical_14_jobs_and_tiny_dispatcher(self):
        result = scan(self.root)
        self.assertEqual(result["status"], "PASS_LIMITED_STATIC_SCOPE", result)
        src = (self.workflows / REUSABLE_NAME).read_text()
        for job in REQUIRED_FULL_JOBS:
            self.assertEqual(src.count("  " + job + ":"), 1)
        self.assertIn("test_private_evidence_batch.py", src)
        for guarded in ("test_public_source_history.py",
                        "test_public_ci_workflow_trust.py",
                        "test_external_ai_boundary.py",
                        "test_national_launch_blockers.py",
                        "playwright install",
                        "test_real_center_demo_http.py",
                        "Taranom.ClinicalEvidenceJournal.IntegrationTests"):
            self.assertIn(guarded, src)
        self.assertLess((self.workflows / DISPATCHER_NAME).stat().st_size, 1000)

    def test_foreign_workflow_call_is_denied(self):
        fake = self.workflows / "rogue.yml"
        fake.write_text((self.workflows / REUSABLE_NAME).read_text())
        self.assert_denied("UNAUTHORIZED_WORKFLOW_CALL")

    def test_canonical_must_have_only_workflow_call_trigger(self):
        self.edit(REUSABLE_NAME, "  workflow_call:", "  workflow_call:\n  push:")
        self.assert_denied("UNAUTHORIZED_WORKFLOW_CALL")

    def test_missing_required_job_is_denied(self):
        self.edit(REUSABLE_NAME, "  member-web-sprint02-regression:",
                  "  renamed-member-web-regression:")
        self.assert_denied("REQUIRED_FULL_CI_JOB_MISSING_OR_EXTRA")

    def test_canonical_job_skip_and_continue_on_error_denied(self):
        self.edit(REUSABLE_NAME,
                  "  internal-contract-tests:\n    runs-on:",
                  "  internal-contract-tests:\n    if: false\n    runs-on:")
        self.assert_denied("REUSABLE_JOB_SKIP_OR_SECRET_FORBIDDEN")
        self.edit(REUSABLE_NAME, "    if: false", "    continue-on-error: true")
        self.assert_denied("REUSABLE_JOB_SKIP_OR_SECRET_FORBIDDEN")

    def test_caller_cannot_use_unapproved_remote_or_ref(self):
        self.edit(DISPATCHER_NAME,
                  "uses: ./.github/workflows/" + REUSABLE_NAME,
                  "uses: attacker/reusable/.github/workflows/other.yml@main")
        self.assert_denied("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_caller_cannot_forward_secrets_or_inputs(self):
        self.edit(DISPATCHER_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME +
                  "\n    secrets: inherit")
        self.assert_denied("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_caller_cannot_skip_gates(self):
        self.edit(DISPATCHER_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME,
                  "    if: false\n    uses: ./.github/workflows/" + REUSABLE_NAME)
        self.assert_denied("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_caller_cannot_request_write_rights(self):
        self.edit(DISPATCHER_NAME, "contents: read", "contents: write")
        self.assert_denied("WORKFLOW_TOKEN_NOT_READ_ONLY")

    def test_caller_rejects_hidden_extra_step_and_runner(self):
        self.edit(DISPATCHER_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME +
                  "\n    runs-on: ubuntu-latest")
        self.assert_denied("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_reusable_secret_expr_stays_denied(self):
        self.edit(REUSABLE_NAME, "    runs-on: ubuntu-latest",
                  "    runs-on: ubuntu-latest\n    env:\n      LEAK: ${{ secrets.BAD }}")
        self.assert_denied("SECRET_EXPRESSION_FORBIDDEN")

    def test_dispatcher_retains_only_three_safe_triggers(self):
        self.edit(DISPATCHER_NAME, "  workflow_dispatch:",
                  "  pull_request_target:")
        self.assert_denied("UNSAFE_OR_UNKNOWN_WORKFLOW_EVENT")

    def test_full_repo_scan_checks_new_files(self):
        result = scan(ROOT)
        self.assertEqual(result["status"], "PASS_LIMITED_STATIC_SCOPE", result)


if __name__ == "__main__":
    unittest.main()
