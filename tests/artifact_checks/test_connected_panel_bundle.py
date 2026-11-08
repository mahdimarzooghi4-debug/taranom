"""Fail-closed synthetic tests for the connected UI distribution verifier."""
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/artifact_checks"))
import check_connected_panel_bundle as connected

CSP = ("default-src 'none'; base-uri 'none'; object-src 'none'; "
       "frame-src 'none'; form-action 'none'; script-src 'self'; "
       "style-src 'self'; font-src 'self'; connect-src 'self'; "
       "worker-src 'none'; media-src 'none'; img-src 'self' data:; "
       "manifest-src 'self'")


class ProtectedBundleTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name) / "dist"
        (self.root / "assets/install").mkdir(parents=True)
        (self.root / "assets/index.js").write_text("console.log('synthetic')", encoding="utf-8")
        (self.root / "assets/index.css").write_text("body{direction:rtl}", encoding="utf-8")
        (self.root / "assets/install/icon.png").write_bytes(b"synthetic-local-icon")
        self.html = (
            '<!doctype html><html lang="fa" dir="rtl"><head>'
            '<meta http-equiv="Content-Security-Policy" content="' + CSP + '">'
            '<link rel="manifest" href="/manifest.webmanifest">'
            '<link rel="icon" href="/assets/install/icon.png">'
            '<link rel="stylesheet" href="/assets/index.css">'
            '</head><body><script type="module" src="/assets/index.js">'
            '</script></body></html>'
        )
        (self.root / "index.html").write_text(self.html, encoding="utf-8")
        self.manifest = ('{"scope":"/","display":"standalone",'
                         '"start_url":"/patient-app/account",'
                         '"icons":[{"src":"/assets/install/icon.png","type":"image/png"}]}')
        (self.root / "manifest.webmanifest").write_text(self.manifest, encoding="utf-8")

    def test_byte_inventory_and_no_release_authority(self):
        result = connected.verify(self.root, "member")
        self.assertEqual(result["source_mode"], "PROTECTED_SAME_ORIGIN_TEST_ONLY")
        self.assertFalse(result["release_allowed"])
        self.assertEqual(len(result["bundle_sha256"]), 64)
        (self.root / "assets/index.js").write_text("changed", encoding="utf-8")
        self.assertNotEqual(result["bundle_sha256"], connected.verify(
            self.root, "member")["bundle_sha256"])

    def test_correct_center_without_membership_manifest(self):
        (self.root / "manifest.webmanifest").unlink()
        (self.root / "index.html").write_text(
            self.html.replace('<link rel="manifest" href="/manifest.webmanifest">', ''),
            encoding="utf-8")
        self.assertEqual(connected.verify(self.root, "center")["surface"], "center")

    def test_remote_or_unbounded_connect_denied(self):
        for bad in ("connect-src *", "connect-src https://outside.invalid",
                    "connect-src 'self' https://outside.invalid",
                    "connect-src 'none'"):
            self.assertFalse(connected.csp_is_same_origin(
                CSP.replace("connect-src 'self'", bad)))

    def test_remote_or_traversal_assets_denied(self):
        for ref in ("https://outside.invalid/p.png", "//outside.invalid",
                    "/assets/../secrets", "/assets/%2e%2e/x", "/assets/?q=1",
                    "/assets//icon.png", "data:text/html,x"):
            with self.assertRaises(ValueError):
                connected.local_path(ref)
        (self.root / "assets/index.css").write_text(
            "body{background:url(https://outside.invalid/a.png)}", encoding="utf-8")
        with self.assertRaises(ValueError):
            connected.verify(self.root, "member")

    def test_unknown_static_asset_or_link_denied(self):
        (self.root / "assets/bundle.map").write_text("secret", encoding="utf-8")
        with self.assertRaises(ValueError):
            connected.verify(self.root, "member")
        (self.root / "assets/bundle.map").unlink()
        (self.root / "index.html").write_text(
            self.html.replace("href=\"/assets/index.css\"",
                              "href=\"https://outside.invalid/style.css\""), encoding="utf-8")
        with self.assertRaises(ValueError):
            connected.verify(self.root, "member")

    def test_manifest_external_icon_and_duplicate_key_denied(self):
        (self.root / "manifest.webmanifest").write_text(
            self.manifest.replace("/assets/install/icon.png", "https://outside.invalid/icon.png"),
            encoding="utf-8")
        with self.assertRaises(ValueError):
            connected.verify(self.root, "member")
        (self.root / "manifest.webmanifest").write_text(
            self.manifest.replace('"scope":"/"', '"scope":"/","scope":"/"'),
            encoding="utf-8")
        with self.assertRaises(ValueError):
            connected.verify(self.root, "member")

    def test_linked_dist_or_nested_file_denied(self):
        (self.root / "assets/alias.png").symlink_to(self.root / "assets/install/icon.png")
        with self.assertRaises(ValueError):
            connected.verify(self.root, "member")
        shutil.rmtree(self.root / "assets/alias.png") if False else (
            self.root / "assets/alias.png").unlink()
        linked = self.root.parent / "linked"
        linked.symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(ValueError):
            connected.bundle_files(linked)


if __name__ == "__main__":
    unittest.main()
