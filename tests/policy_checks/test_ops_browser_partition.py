"""Ensure all protected portal browser tests remain in full CI on the right host.

The legacy localhost ASP.NET Ops host at 4318 does not serve privileged
Admin/Support React routes. The 4319 Vite preview runs those tests against
intercepted test responses; it is NOT a production authentication host.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
CONNECTED = frozenset({
    "support-desk-api.spec.ts",
    "admin-centers-api.spec.ts",
    "admin-manager-requests-api.spec.ts",
})


def _list_literal(source: str, key: str) -> frozenset[str]:
    pattern = r"\b" + re.escape(key) + r":\[([^\]]+)\]"
    matches = re.findall(pattern, source)
    if len(matches) != 1:
        raise AssertionError("Expected exactly one browser partition: " + key)
    body = matches[0]
    values = re.findall(r"'([a-z-]+\.spec\.ts)'", body)
    if not values or body.count("'") != len(values) * 2:
        raise AssertionError("Unexpected browser filename list")
    return frozenset(values)


class TestOpsBrowserPartition(unittest.TestCase):
    def test_two_explicit_hosts_keep_all_protected_tests(self):
        default = (ROOT / "apps/ops-web/playwright.config.ts").read_text()
        protected = (ROOT / "apps/ops-web/playwright.support.config.ts").read_text()
        self.assertEqual(CONNECTED, _list_literal(default, "testIgnore"))
        self.assertEqual(CONNECTED, _list_literal(protected, "testMatch"))
        for name in CONNECTED:
            self.assertTrue((ROOT / "apps/ops-web/tests" / name).is_file())
        self.assertIn("http://127.0.0.1:4318", default)
        self.assertIn("http://127.0.0.1:4319", protected)
        self.assertIn("reuseExistingServer:!process.env.CI", protected)

    def test_canonical_full_regression_invokes_both_suites(self):
        path = ROOT / ".github/workflows/taranom-full-regression-reusable.yml"
        workflow = path.read_text()
        self.assertIn(
            "cd apps/ops-web\n          CI=true npm run test:e2e\n",
            workflow,
        )
        self.assertIn(
            "CI=true npm run test:e2e -- --config playwright.support.config.ts",
            workflow,
        )


if __name__ == "__main__":
    unittest.main()
