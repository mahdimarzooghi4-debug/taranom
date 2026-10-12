"""TRN-TECH-096: mandatory exact-main read-only full-14-job CI dispatcher."""
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/policy_checks"))
from check_public_ci_workflow_trust import (
    REUSABLE_NAME, MAINLINE_DISPATCHER_NAME, REQUIRED_FULL_JOBS, scan
)


class MainlineFullCiTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.workflows = self.root / ".github" / "workflows"
        self.workflows.mkdir(parents=True)
        for name in (REUSABLE_NAME, MAINLINE_DISPATCHER_NAME):
            shutil.copy2(ROOT / ".github" / "workflows" / name, self.workflows / name)

    def deny(self, rule):
        report = scan(self.root)
        self.assertEqual("BLOCKED", report["status"], report)
        self.assertIn(rule, {v["rule"] for v in report["findings"]})
        self.assertFalse(report["release_allowed"])
        self.assertFalse(report["security_clearance"])

    def edit(self, a, b):
        p = self.workflows / MAINLINE_DISPATCHER_NAME
        value = p.read_text()
        self.assertIn(a, value)
        p.write_text(value.replace(a, b, 1))

    def test_main_branch_push_and_pr_use_canonical_full_14(self):
        result = scan(self.root)
        self.assertEqual("PASS_LIMITED_STATIC_SCOPE", result["status"], result)
        text = (self.workflows / MAINLINE_DISPATCHER_NAME).read_text()
        self.assertIn("    branches: [main]", text)
        self.assertEqual(2, text.count("    branches: [main]"))
        self.assertLess(len(text), 1000)
        self.assertEqual(14, len(REQUIRED_FULL_JOBS))
        self.assertEqual(
            1, text.count("uses: ./.github/workflows/" + REUSABLE_NAME))
        self.assertFalse(result["release_allowed"])

    def test_mainline_file_is_mandatory_not_a_silent_missing_check(self):
        (self.workflows / MAINLINE_DISPATCHER_NAME).unlink()
        self.deny("MANDATORY_MAINLINE_CI_MISSING")

    def test_mainline_cannot_be_renamed_out_of_required_path(self):
        (self.workflows / MAINLINE_DISPATCHER_NAME).rename(
            self.workflows / "renamed.yml")
        self.deny("MANDATORY_MAINLINE_CI_MISSING")

    def test_main_push_target_cannot_change_to_feature_branch(self):
        self.edit("  push:\n    branches: [main]",
                  "  push:\n    branches: [sprint-87-mainline-ci-integrity]")
        self.deny("DISPATCHER_TARGET_BRANCHES_CHANGED")

    def test_main_pr_target_cannot_change_to_feature_branch(self):
        self.edit("  pull_request:\n    branches: [main]",
                  "  pull_request:\n    branches: [sprint-87-mainline-ci-integrity]")
        self.deny("DISPATCHER_TARGET_BRANCHES_CHANGED")

    def test_mainline_rejects_pull_request_target_privilege(self):
        self.edit("  pull_request:", "  pull_request_target:")
        self.deny("DISPATCHER_TRIGGERS_CHANGED")

    def test_mainline_rejects_write_scopes(self):
        self.edit("  contents: read", "  contents: write")
        self.deny("WORKFLOW_TOKEN_NOT_READ_ONLY")

    def test_mainline_rejects_remote_pinned_reusable(self):
        self.edit("uses: ./.github/workflows/" + REUSABLE_NAME,
                  "uses: outside/repo/.github/workflows/full.yml@main")
        self.deny("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_mainline_rejects_secret_forwarding(self):
        self.edit("    uses: ./.github/workflows/" + REUSABLE_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME +
                  "\n    secrets: inherit")
        self.deny("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_mainline_rejects_conditional_skip(self):
        self.edit("    uses: ./.github/workflows/" + REUSABLE_NAME,
                  "    if: false\n    uses: ./.github/workflows/" + REUSABLE_NAME)
        self.deny("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_mainline_rejects_extra_job(self):
        self.edit("    uses: ./.github/workflows/" + REUSABLE_NAME,
                  "    uses: ./.github/workflows/" + REUSABLE_NAME +
                  "\n  additional:\n    runs-on: ubuntu-latest")
        self.deny("UNSAFE_OR_INVALID_REUSABLE_CALL")

    def test_real_repository_has_mandatory_ci(self):
        result = scan(ROOT)
        self.assertEqual("PASS_LIMITED_STATIC_SCOPE", result["status"], result)
        self.assertFalse(result["security_clearance"])


if __name__ == "__main__":
    unittest.main()
