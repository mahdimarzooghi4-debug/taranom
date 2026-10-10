"""Negative and replay tests for built disconnected HTML/CSS/JS output."""
from __future__ import annotations

import json
from pathlib import Path
import tempfile
import unittest

from tools.artifact_checks.check_disconnected_bundle import (
    main, verify_bundle,
)


ENTRY = """<!doctype html>
<html lang="fa" dir="rtl"><head>
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; connect-src 'none'; script-src 'self'; form-action 'none'">
<script type="module" crossorigin src="/assets/index-1234.js"></script>
<link rel="stylesheet" crossorigin href="/assets/index-5678.css">
</head><body><div id="root"></div></body></html>
"""


class BuiltBundleIntegrityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory(prefix="trn-built-artifact-")
        self.root = Path(self.tmp.name)
        self.dist = self.root / "dist"
        self.dist.mkdir()
        self.write("index.html", ENTRY)
        self.write("assets/index-1234.js", "document.querySelector('#root');\n")
        self.write("assets/index-5678.css", "body{color:#222}\n")

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def write(self, path: str, content: str) -> None:
        destination = self.dist / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(content, encoding="utf-8")

    def reject(self) -> None:
        with self.assertRaises((ValueError, UnicodeError, OSError)):
            verify_bundle(self.dist, "member")

    def test_complete_disconnected_member_and_center_output_has_only_unapproved_receipt(self) -> None:
        for surface in ("member", "center"):
            with self.subTest(surface=surface):
                value = verify_bundle(self.dist, surface)
                self.assertEqual(surface, value["surface"])
                self.assertIs(value["release_allowed"], False)
                self.assertEqual(3, len(value["files"]))
                self.assertEqual(64, len(value["bundle_sha256"]))

    def test_hashes_are_deterministic_and_path_sorted(self) -> None:
        one = verify_bundle(self.dist, "member")
        two = verify_bundle(self.dist, "member")
        self.assertEqual(one, two)
        self.assertEqual(
            ["assets/index-1234.js", "assets/index-5678.css", "index.html"],
            [x["path"] for x in one["files"]])

    def test_modified_js_changes_digest_even_if_html_is_unchanged(self) -> None:
        previous = verify_bundle(self.dist, "member")
        self.write("assets/index-1234.js", "document.querySelector('#root'); /* modified */")
        current = verify_bundle(self.dist, "member")
        self.assertNotEqual(previous["bundle_sha256"], current["bundle_sha256"])

    def test_missing_html_or_asset_fails(self) -> None:
        (self.dist / "assets/index-1234.js").unlink()
        self.reject()
        self.write("assets/index-1234.js", "const hi=1")
        (self.dist / "index.html").unlink()
        self.reject()

    def test_missing_stylesheet_fails(self) -> None:
        (self.dist / "assets/index-5678.css").unlink()
        self.reject()

    def test_external_js_source_is_denied(self) -> None:
        self.write("index.html", ENTRY.replace("/assets/index-1234.js",
                                               "https://cdn.example.com/out.js"))
        self.reject()

    def test_protocol_relative_css_source_is_denied(self) -> None:
        self.write("index.html", ENTRY.replace(
            "/assets/index-5678.css", "//cdn.example.com/style.css"))
        self.reject()

    def test_active_query_parameters_cannot_bypass_validation(self) -> None:
        self.write("index.html", ENTRY.replace(
            "/assets/index-1234.js", "/assets/index-1234.js?v=1"))
        self.reject()

    def test_percent_encoded_or_parent_traversal_assets_are_denied(self) -> None:
        for value in ("/assets/%2e%2e/secret.js", "/assets/../secret.js"):
            with self.subTest(value=value):
                self.write("index.html", ENTRY.replace("/assets/index-1234.js", value))
                self.reject()

    def test_inline_or_untyped_script_is_denied(self) -> None:
        for code in ('<script>fetch("/private");</script>',
                     '<script src="/assets/index-1234.js"></script>'):
            with self.subTest(code=code):
                self.write("index.html", ENTRY.replace(
                    '<script type="module" crossorigin src="/assets/index-1234.js"></script>',
                    code))
                self.reject()

    def test_duplicate_csp_and_missing_network_deny_are_denied(self) -> None:
        csp = "default-src 'none'; connect-src 'none'; script-src 'self'; form-action 'none'"
        self.write("index.html", ENTRY.replace(
            "connect-src 'none'", "connect-src 'self'"))
        self.reject()
        self.write("index.html", ENTRY.replace("</head>",
            f'<meta http-equiv="Content-Security-Policy" content="{csp}"></head>'))
        self.reject()

    def test_non_persian_or_non_rtl_page_is_denied(self) -> None:
        self.write("index.html", ENTRY.replace('lang="fa"', 'lang="en"'))
        self.reject()
        self.write("index.html", ENTRY.replace('dir="rtl"', 'dir="ltr"'))
        self.reject()

    def test_hidden_form_iframe_and_refresh_are_denied(self) -> None:
        for code in ('<form action="/login"></form>',
                     '<iframe src="/member"></iframe>',
                     '<meta http-equiv="refresh" content="0;url=https://x.example">'):
            with self.subTest(code=code):
                self.write("index.html", ENTRY.replace("</body>", f"{code}</body>"))
                self.reject()

    def test_css_remote_import_and_url_are_denied(self) -> None:
        for css in ('@import "https://cdn.example.com/main.css";',
                    'div{background:url(https://tracker.example.com/i.png)}',
                    'div{background:url(//cdn.example.com/i.png)}'):
            with self.subTest(css=css):
                self.write("assets/index-5678.css", css)
                self.reject()

    def test_local_css_image_references_are_allowed_only_if_in_dist(self) -> None:
        self.write("assets/index-5678.css",
                   'body{background:url("/assets/icon.svg")}')
        self.reject()
        self.write("assets/icon.svg", "<svg></svg>")
        value = verify_bundle(self.dist, "member")
        self.assertEqual(4, len(value["files"]))

    def test_sourcemap_file_is_not_distributable(self) -> None:
        self.write("assets/index-1234.js.map", '{"sources":["src/main.tsx"]}')
        self.reject()

    def test_js_sourcemap_comment_is_denied_without_a_map_file(self) -> None:
        self.write("assets/index-1234.js", "//# sourceMappingURL=main.js.map")
        self.reject()

    def test_unapproved_file_extension_is_denied(self) -> None:
        self.write("assets/unexpected.json", '{"private": "unknown"}')
        self.reject()

    def test_symlinked_content_is_denied(self) -> None:
        link = self.dist / "assets" / "redirect.js"
        link.symlink_to(self.dist / "assets" / "index-1234.js")
        self.reject()

    def test_symlinked_directory_is_denied(self) -> None:
        (self.dist / "alias").symlink_to(self.dist / "assets", target_is_directory=True)
        self.reject()

    def test_malformed_js_encoding_is_denied(self) -> None:
        (self.dist / "assets/index-1234.js").write_bytes(b"\xff\xfe")
        self.reject()

    def test_invalid_surface_is_denied(self) -> None:
        with self.assertRaises(ValueError):
            verify_bundle(self.dist, "production")

    def test_manifest_roundtrip_and_tamper_fail_closed(self) -> None:
        report = self.root / "evidence" / "bundle.json"
        self.assertEqual(0, main(["--dist", str(self.dist), "--surface", "member",
                                  "--output", str(report)]))
        self.assertEqual(0, main(["--dist", str(self.dist), "--surface", "member",
                                  "--verify", str(report)]))
        stored = json.loads(report.read_text(encoding="utf-8"))
        self.assertIs(stored["release_allowed"], False)
        self.write("assets/index-1234.js", "tampered")
        self.assertEqual(1, main(["--dist", str(self.dist), "--surface", "member",
                                  "--verify", str(report)]))

    def test_cross_surface_manifest_replay_is_denied(self) -> None:
        report = self.root / "evidence" / "bundle.json"
        self.assertEqual(0, main(["--dist", str(self.dist), "--surface", "member",
                                  "--output", str(report)]))
        self.assertEqual(1, main(["--dist", str(self.dist), "--surface", "center",
                                  "--verify", str(report)]))

    def test_reused_manifest_with_forged_release_permission_is_denied(self) -> None:
        report = self.root / "evidence" / "bundle.json"
        self.assertEqual(0, main(["--dist", str(self.dist), "--surface", "member",
                                  "--output", str(report)]))
        stored = json.loads(report.read_text(encoding="utf-8"))
        stored["release_allowed"] = True
        report.write_text(json.dumps(stored), encoding="utf-8")
        self.assertEqual(1, main(["--dist", str(self.dist), "--surface", "member",
                                  "--verify", str(report)]))

    def test_duplicate_manifest_key_is_denied(self) -> None:
        report = self.root / "fake.json"
        report.write_text('{"surface":"member","surface":"center"}', encoding="utf-8")
        self.assertEqual(1, main(["--dist", str(self.dist), "--surface", "member",
                                  "--verify", str(report)]))

    def test_manifest_cannot_be_written_into_dist(self) -> None:
        report = self.dist / "attestation.json"
        self.assertEqual(1, main(["--dist", str(self.dist), "--surface", "member",
                                  "--output", str(report)]))
        self.assertFalse(report.exists())


if __name__ == "__main__":
    unittest.main()
