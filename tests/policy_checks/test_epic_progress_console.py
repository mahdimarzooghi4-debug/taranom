"""Functional offline Epic console tests — data integrity, output security and usability."""
from __future__ import annotations

import csv
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "policy_checks"))
import render_epic_progress_console as console  # noqa: E402
from check_epic_capability_progress import REGISTRY, BLOCKERS  # noqa: E402


class Tags(HTMLParser):
    def __init__(self):
        super().__init__()
        self.seen = []
    def handle_starttag(self, tag, attrs):
        self.seen.append((tag, dict(attrs)))


class ConsoleTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "repo"
        self.root.mkdir()
        for rel in (REGISTRY, BLOCKERS):
            target = self.root / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / rel, target)
        self.registry = json.loads((self.root / REGISTRY).read_text())
        for cap in self.registry["capabilities"]:
            for key in ("implementation_ref", "test_ref"):
                if cap[key]:
                    path = self.root / cap[key]
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text("test-only", encoding="utf-8")
        self.html = Path(self.tmp.name) / "console.html"
        self.csv = Path(self.tmp.name) / "console.csv"

    def invoke(self):
        return console.main(["--root", str(self.root),
                             "--html", str(self.html), "--csv", str(self.csv)])

    def test_current_repository_can_generate_usable_outputs(self):
        with tempfile.TemporaryDirectory() as dest:
            page = Path(dest) / "console.html"
            data = Path(dest) / "console.csv"
            self.assertEqual(console.main([
                "--root", str(ROOT), "--html", str(page), "--csv", str(data)]), 0)
            self.assertTrue(page.is_file())
            self.assertTrue(data.is_file())

    def test_both_exports_contain_thirteen_epics_and_national_scopes(self):
        self.assertEqual(self.invoke(), 0)
        page = self.html.read_text()
        dom = Tags()
        dom.feed(page)
        epics = [attrs for tag, attrs in dom.seen if "data-epic" in attrs]
        self.assertEqual(len(epics), 13)
        for idx in range(1, 14):
            self.assertIn(f"TRN-EP-{idx:02d}", page)
        for dimension in console.OTHER_DIMENSIONS:
            self.assertIn(dimension, page)

    def test_csv_has_thirty_nine_slots_and_four_independent_dimensions(self):
        self.assertEqual(self.invoke(), 0)
        with self.csv.open(newline="") as f:
            rows = list(csv.DictReader(f))
        self.assertEqual(len(rows), 43)
        self.assertEqual(len([r for r in rows if r["row_type"] == "CANDIDATE"]), 39)
        self.assertEqual(len([r for r in rows if r["row_type"] == "MANDATORY_RELEASE_DIMENSION"]), 4)
        self.assertTrue(all(r["formal_acceptance"] == "NOT_VERIFIED" for r in rows))

    def test_html_states_unapproved_baseline_and_no_real_completion(self):
        self.assertEqual(self.invoke(), 0)
        text = self.html.read_text()
        self.assertIn("DRAFT_UNAPPROVED", text)
        self.assertIn("formal_acceptance_pct: null", text)
        self.assertIn("DENIED_BY_DEFAULT", text)
        self.assertIn("BLOCKED_UNVERIFIED", text)
        self.assertIn("۲۰٪ مبنای رسمی ندارد", text)

    def test_no_remote_content_or_network_needed(self):
        self.assertEqual(self.invoke(), 0)
        text = self.html.read_text()
        for token in ('http://', 'https://', '<iframe', '<img', '<link', '<form', 'fetch(', 'XMLHttpRequest', 'localStorage'):
            self.assertNotIn(token, text)
        self.assertIn("connect-src &#x27;none&#x27;", text)
        self.assertIn("script-src &#x27;sha256-", text)

    def test_search_and_evidence_filter_present(self):
        self.assertEqual(self.invoke(), 0)
        parser = Tags()
        parser.feed(self.html.read_text())
        ids = {a.get("id") for _, a in parser.seen}
        self.assertIn("search", ids)
        self.assertIn("evidence", ids)
        self.assertIn("filter-result", ids)
        self.assertEqual(
            len([a for _, a in parser.seen if "data-capability" in a]), 39)

    def test_file_and_test_refs_visible_without_reclassifying_acceptance(self):
        self.assertEqual(self.invoke(), 0)
        text = self.html.read_text()
        self.assertIn("IdentityProofingReview.cs", text)
        self.assertIn("IdentityProofingReviewTests.cs", text)
        self.assertIn("NOT_VERIFIED", text)
        self.assertNotIn("PRODUCTION_READY", text)

    def test_html_escapes_untrusted_labels(self):
        self.registry["capabilities"][0]["capability"] = '<script>alert("x")</script>'
        (self.root / REGISTRY).write_text(json.dumps(self.registry), encoding="utf-8")
        self.assertEqual(self.invoke(), 0)
        rendered = self.html.read_text()
        self.assertNotIn('<script>alert("x")</script>', rendered)
        self.assertIn('&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt;', rendered)

    def test_html_escapes_quotes_in_dom_attributes(self):
        self.registry["capabilities"][0]["capability"] = '" role="admin"'
        (self.root / REGISTRY).write_text(json.dumps(self.registry), encoding="utf-8")
        self.assertEqual(self.invoke(), 0)
        rendered = self.html.read_text()
        self.assertIn('&quot; role=&quot;admin&quot;', rendered)
        self.assertEqual(rendered.count('data-epic-name='), 13)

    def test_spreadsheet_formula_injection_is_escaped(self):
        self.assertEqual(console.csv_safe("  =HYPERLINK(x)"), "'=HYPERLINK(x)")
        self.assertEqual(console.csv_safe("+SUM(1)"), "'+SUM(1)")
        self.assertEqual(console.csv_safe("@SUM(1)"), "'@SUM(1)")
        self.assertEqual(console.csv_safe("-1"), "'-1")
        self.assertEqual(console.csv_safe("plain"), "plain")

    def test_actual_csv_export_escapes_formula_cell(self):
        self.registry["capabilities"][0]["capability"] = "=HYPERLINK(H1)"
        (self.root / REGISTRY).write_text(json.dumps(self.registry), encoding="utf-8")
        self.assertEqual(self.invoke(), 0)
        with self.csv.open(newline="") as f:
            rows = list(csv.DictReader(f))
        self.assertEqual(rows[0]["label"], "'=HYPERLINK(H1)")

    def test_missing_epic_input_denies_and_writes_nothing(self):
        self.registry["capabilities"] = self.registry["capabilities"][:-3]
        (self.root / REGISTRY).write_text(json.dumps(self.registry), encoding="utf-8")
        self.assertEqual(self.invoke(), 1)
        self.assertFalse(self.html.exists())
        self.assertFalse(self.csv.exists())

    def test_verified_denied_blocker_required(self):
        national = json.loads((self.root / BLOCKERS).read_text())
        national["release_admission"] = "ALLOWED"
        (self.root / BLOCKERS).write_text(json.dumps(national), encoding="utf-8")
        self.assertEqual(self.invoke(), 1)
        self.assertFalse(self.html.exists())

    def test_no_working_network_or_secrets_present(self):
        self.assertEqual(self.invoke(), 0)
        text = self.html.read_text() + self.csv.read_text()
        self.assertNotIn("Bearer ", text)
        self.assertNotIn("PRIVATE KEY", text)
        self.assertNotIn("NationalId", text)
        self.assertNotIn("real_patient", text)

    def test_digest_is_stable_for_same_sources(self):
        digest = console.source_digest(self.root)
        self.assertEqual(len(digest), 64)
        self.assertEqual(self.invoke(), 0)
        first_html, first_csv = self.html.read_bytes(), self.csv.read_bytes()
        self.assertEqual(self.invoke(), 0)
        self.assertEqual(self.html.read_bytes(), first_html)
        self.assertEqual(self.csv.read_bytes(), first_csv)
        self.assertIn(digest, first_html.decode())

    def test_digest_changes_after_provenance_change_without_approval(self):
        before = console.source_digest(self.root)
        self.registry["capabilities"][0]["capability"] = "Changed candidate wording only"
        (self.root / REGISTRY).write_text(json.dumps(self.registry), encoding="utf-8")
        after = console.source_digest(self.root)
        self.assertNotEqual(before, after)
        self.assertEqual(self.invoke(), 0)
        self.assertIn(after, self.html.read_text())

    def test_refuses_output_overwriting_governance_source(self):
        rc = console.main(["--root", str(self.root), "--html",
                           str(self.root / REGISTRY), "--csv", str(self.csv)])
        self.assertEqual(rc, 1)
        self.assertFalse(self.csv.exists())
        self.assertEqual(json.loads((self.root / REGISTRY).read_text())["schema_version"], 1)

    def test_same_html_and_csv_output_rejected(self):
        rc = console.main(["--root", str(self.root), "--html",
                           str(self.html), "--csv", str(self.html)])
        self.assertEqual(rc, 1)
        self.assertFalse(self.html.exists())

    def test_html_csp_contains_real_sha256_of_embedded_scripts(self):
        import base64
        self.assertEqual(self.invoke(), 0)
        page = self.html.read_text()
        js_hash = base64.b64encode(hashlib.sha256(console.JS.encode()).digest()).decode()
        css_hash = base64.b64encode(hashlib.sha256(console.CSS.encode()).digest()).decode()
        self.assertIn("script-src &#x27;sha256-" + js_hash, page)
        self.assertIn("style-src &#x27;sha256-" + css_hash, page)

    def test_no_inferred_percent_from_source_files(self):
        assessment, draft, blockers = console.load_verified(self.root)
        self.assertIsNone(assessment["overall_implementation_completion_pct"])
        self.assertFalse(assessment["national_release_allowed"])
        self.assertEqual(assessment["references_to_test_only_or_disconnected_code"], 26)
        self.assertEqual(assessment["capabilities_without_code_test_links"], 13)
        self.assertEqual(len(draft["capabilities"]), 39)
        self.assertEqual(len(blockers["items"]), 17)


if __name__ == "__main__":
    unittest.main()
