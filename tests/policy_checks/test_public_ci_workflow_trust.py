"""TRN-TECH-066: actual in-repo workflows and temporary unsafe CI fixtures."""
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/policy_checks"))
from check_public_ci_workflow_trust import scan  # noqa: E402

SHA = "11bd71901bbe5b1630ceea73d27597364c9af683"
BASE = f"""name: Public CI
on:
  push:
    branches: [test]
  pull_request:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@{SHA} # pinned
        with:
          persist-credentials: false
      - name: Test
        run: python3 -m unittest discover
"""


class WorkflowTrustTests(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root = Path(t.name)
        self.dir = self.root / ".github/workflows"
        self.dir.mkdir(parents=True)
        self.file = self.dir / "check.yml"
        self.file.write_text(BASE, encoding="utf-8")

    def check(self, rule=None):
        result = scan(self.root)
        self.assertFalse(result["release_allowed"])
        self.assertFalse(result["security_clearance"])
        if rule is None:
            self.assertEqual(result["status"], "PASS_LIMITED_STATIC_SCOPE", result)
        else:
            self.assertEqual(result["status"], "BLOCKED")
            self.assertIn(rule, [x["rule"] for x in result["findings"]])
        return result

    def change(self, old, new):
        s = self.file.read_text()
        self.assertIn(old, s)
        self.file.write_text(s.replace(old, new), encoding="utf-8")

    def test_good_workflow_is_static_pass_not_release(self):
        self.check()

    def test_actual_project_workflows_are_checked(self):
        result = scan(ROOT)
        self.assertEqual(result["status"], "PASS_LIMITED_STATIC_SCOPE", result)
        self.assertGreaterEqual(result["workflow_count"], 56)
        self.assertFalse(result["release_allowed"])

    def test_unpinned_checkout_rejected(self):
        self.change("actions/checkout@" + SHA, "actions/checkout@v4")
        self.check("UNPINNED_OR_UNAPPROVED_ACTION")

    def test_unapproved_third_party_action_rejected_even_with_pin(self):
        self.change("actions/checkout@", "untrusted/checkout@")
        self.check("UNPINNED_OR_UNAPPROVED_ACTION")

    def test_dynamic_action_rejected(self):
        self.change("actions/checkout@" + SHA, "actions/checkout@\${{ inputs.ref }}")
        self.check("DYNAMIC_OR_INVALID_ACTION")

    def test_token_persistence_rejected(self):
        self.change("persist-credentials: false", "persist-credentials: true")
        self.check("CHECKOUT_PERSISTS_CREDENTIALS")

    def test_missing_persistence_line_rejected(self):
        self.change("          persist-credentials: false\n", "")
        self.check("CHECKOUT_PERSISTS_CREDENTIALS")

    def test_workflow_write_scope_rejected(self):
        self.change("contents: read", "contents: write")
        self.check("WORKFLOW_TOKEN_NOT_READ_ONLY")

    def test_job_scope_override_rejected(self):
        self.change("    runs-on: ubuntu-latest", "    permissions:\n      contents: write\n    runs-on: ubuntu-latest")
        self.check("JOB_PERMISSION_OVERRIDE")

    def test_unsafe_pull_request_target_rejected(self):
        self.change("  pull_request:", "  pull_request_target:")
        self.check("UNSAFE_OR_UNKNOWN_WORKFLOW_EVENT")

    def test_repository_dispatch_rejected(self):
        self.change("  push:", "  repository_dispatch:")
        self.check("UNSAFE_OR_UNKNOWN_WORKFLOW_EVENT")

    def test_secret_expression_rejected(self):
        self.change("run: python3 -m unittest discover",
                    "run: echo '\${{ secrets.PRIVATE_TOKEN }}'")
        self.check("SECRET_EXPRESSION_FORBIDDEN")

    def test_unknown_hosted_runner_rejected(self):
        self.change("runs-on: ubuntu-latest", "runs-on: self-hosted")
        self.check("UNAPPROVED_JOB_RUNNER")

    def test_deploy_environment_rejected(self):
        self.change("    runs-on: ubuntu-latest",
                    "    environment: production\n    runs-on: ubuntu-latest")
        self.check("UNREVIEWED_ENVIRONMENT_OR_CONTAINER")

    def test_oidc_permission_rejected(self):
        self.change("  contents: read", "  contents: read\n  id-token: write")
        self.check("WORKFLOW_TOKEN_NOT_READ_ONLY")

    def test_symlinked_workflow_rejected(self):
        self.file.unlink()
        self.file.symlink_to(self.root / "missing.yml")
        self.check("UNEXPECTED_WORKFLOW_TREE_ENTRY")

    def test_empty_dir_not_a_pass(self):
        self.file.unlink()
        self.check("NO_WORKFLOWS")

    def test_malformed_workflow_without_permissions_rejected(self):
        self.change("permissions:\n  contents: read\n", "")
        self.check("MISSING_OR_DUPLICATE_REQUIRED_ROOT")

    def test_duplicate_root_permissions_rejected(self):
        self.file.write_text(BASE + "\npermissions:\n  contents: read\n")
        self.check("MISSING_OR_DUPLICATE_REQUIRED_ROOT")

    def test_file_payload_not_echoed_in_report(self):
        self.change("contents: read", "contents: write")
        output = json.dumps(self.check("WORKFLOW_TOKEN_NOT_READ_ONLY"))
        self.assertNotIn("contents: write", output)
        self.assertNotIn(SHA, output)


if __name__ == "__main__":
    unittest.main()
