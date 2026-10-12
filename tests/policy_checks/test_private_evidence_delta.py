"""TRN-TECH-082: actual temporary private bytes, two immutable rounds, denial tests."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/policy_checks"))
import compare_private_evidence_snapshots as reconcile
import build_private_evidence_batch as batch
import check_private_external_evidence as custody


class EvidenceDeltaTests(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.home = Path(t.name)
        self.repo = self.home / "public"
        inventory_dir = self.repo / "docs/governance"
        inventory_dir.mkdir(parents=True)
        shutil.copyfile(ROOT / "docs/governance/national-launch-blockers.json",
                        inventory_dir / "national-launch-blockers.json")
        self.private = self.home / "private"
        self.private.mkdir(mode=0o700)
        self.before = self.private / "before"
        self.after = self.private / "after"
        self.before.mkdir(mode=0o700)
        self.after.mkdir(mode=0o700)
        self.prev_files = self.before / "evidence"
        self.new_files = self.after / "evidence"
        self.prev_files.mkdir(mode=0o700)
        self.new_files.mkdir(mode=0o700)
        self.prev_manifest = self.before / "intake.json"
        self.new_manifest = self.after / "intake.json"
        self.report_dir = self.private / "reports"
        self.report_dir.mkdir(mode=0o700)
        self.report = self.report_dir / "delta.json"
        self.before_entries = [
            self.make_entry(self.prev_files, "private-old-identity-a.bin", b"originalA",
                            "TRN-EP-01", "VERIFIED_MEMBER_IDENTITY"),
            self.make_entry(self.prev_files, "private-old-identity-b.bin", b"originalB",
                            "TRN-EP-01", "VERIFIED_MEMBER_IDENTITY"),
            self.make_entry(self.prev_files, "private-discontinued-support.bin",
                            b"support-old", "MARKET-SUPPORT", "ALL_3_MARKET_OPERATIONS"),
        ]
        self.after_entries = [
            self.make_entry(self.new_files, "renamed-identical.bin", b"originalA",
                            "TRN-EP-01", "VERIFIED_MEMBER_IDENTITY"),
            self.make_entry(self.new_files, "private-updated-identity.bin", b"replacementC",
                            "TRN-EP-01", "VERIFIED_MEMBER_IDENTITY"),
            self.make_entry(self.new_files, "private-new-corporate.bin",
                            b"corporate-new", "MARKET-CORPORATE", "DATA_RIGHTS_AND_CONSENT"),
        ]
        self.write_manifests()

    def make_entry(self, folder, filename, raw, dimension, blocker):
        (folder / filename).write_bytes(raw)
        return {
            "dimension": dimension, "blocker": blocker, "artifact": filename,
            "sha256": hashlib.sha256(raw).hexdigest(),
            "custodian_ref": "private-operator-ref", "review_state": "UNREVIEWED",
        }

    def write_manifest(self, path, entries):
        source = (self.repo / "docs/governance/national-launch-blockers.json").read_bytes()
        path.write_text(json.dumps({
            "schema_version": 1, "scope": custody.SCOPE,
            "source_inventory_sha256": hashlib.sha256(source).hexdigest(),
            "entries": entries
        }), encoding="utf-8")
        path.chmod(0o600)

    def write_manifests(self):
        self.write_manifest(self.prev_manifest, self.before_entries)
        self.write_manifest(self.new_manifest, self.after_entries)

    def compare(self):
        return reconcile.compare(self.repo, self.prev_files, self.prev_manifest,
                                 self.new_files, self.new_manifest, self.report)

    def denied(self, code):
        with self.assertRaises(batch.BatchError) as raised:
            self.compare()
        self.assertEqual(str(raised.exception), code)
        self.assertFalse(self.report.exists())

    def test_real_two_round_delta_and_no_private_identifiers(self):
        report = self.compare()
        self.assertEqual(report["status"], reconcile.STATUS)
        self.assertEqual(report["dimension_count"], 17)
        self.assertEqual(report["changed_blocker_slots"], 3)
        self.assertEqual(report["byte_identical_retained_file_records"], 1)
        self.assertEqual(report["new_or_changed_file_records"], 2)
        self.assertEqual(report["removed_or_replaced_file_records"], 2)
        self.assertFalse(report["release_allowed"])
        self.assertFalse(report["external_evidence_verified"])
        self.assertIsNone(report["overall_product_completion_percentage"])
        self.assertEqual(stat.S_IMODE(self.report.stat().st_mode), 0o600)
        self.assertEqual(len(report["dimensions"]), 17)
        self.assertTrue(all(r["status"] == "BLOCKED_UNVERIFIED"
                            for r in report["dimensions"]))
        def slot(dimension, blocker):
            dim = next(d for d in report["dimensions"] if d["dimension"] == dimension)
            return next(s for s in dim["blocker_slots"] if s["blocker"] == blocker)
        self.assertEqual(slot("TRN-EP-01", "VERIFIED_MEMBER_IDENTITY")["change"],
                         "REPLACED_OR_CHANGED_UNREVIEWED")
        self.assertEqual(slot("MARKET-SUPPORT", "ALL_3_MARKET_OPERATIONS")["change"],
                         "REMOVED_UNREVIEWED")
        self.assertEqual(slot("MARKET-CORPORATE", "DATA_RIGHTS_AND_CONSENT")["change"],
                         "ADDED_UNREVIEWED")
        self.assertEqual(slot("TRN-EP-01", "LEGAL_OPERATOR_IDENTITY")["change"],
                         "NO_FILES_IN_EITHER_ROUND")
        report_text = self.report.read_text()
        for secret in ("private-old-identity", "private-operator-ref",
                       "renamed-identical.bin", "originalA", "replacementC",
                       self.after_entries[0]["sha256"], str(self.private)):
            self.assertNotIn(secret, report_text)
        self.assertEqual(json.loads(report_text), report)

    def test_identical_bytes_under_new_file_name_are_retained_not_added(self):
        self.after_entries = self.after_entries[:1]
        self.before_entries = self.before_entries[:1]
        self.write_manifests()
        r = self.compare()
        self.assertEqual(r["changed_blocker_slots"], 0)
        self.assertEqual(r["byte_identical_retained_file_records"], 1)

    def test_multiple_identical_record_bytes_count_as_records_not_reviewers(self):
        self.before_entries = self.before_entries[:1]
        self.after_entries = self.after_entries[:1] + [
            self.make_entry(self.new_files, "same-content-again.bin", b"originalA",
                            "TRN-EP-01", "VERIFIED_MEMBER_IDENTITY"),
        ]
        self.write_manifests()
        r = self.compare()
        self.assertEqual(r["byte_identical_retained_file_records"], 1)
        self.assertEqual(r["new_or_changed_file_records"], 1)
        self.assertFalse(r["independent_review_observed"])

    def test_changed_old_file_bytes_fails_closed(self):
        (self.prev_files / self.before_entries[0]["artifact"]).write_bytes(b"secret-tampered")
        self.denied("PRIVATE_SNAPSHOT_BYTE_INTEGRITY_UNVERIFIABLE")

    def test_modified_digest_or_self_approval_fails_closed(self):
        self.after_entries[0]["sha256"] = "0" * 64
        self.write_manifests()
        self.denied("PRIVATE_SNAPSHOT_BYTE_INTEGRITY_UNVERIFIABLE")
        self.after_entries[0]["sha256"] = hashlib.sha256(b"originalA").hexdigest()
        self.after_entries[0]["review_state"] = "APPROVED"
        self.write_manifests()
        self.denied("PRIVATE_SNAPSHOT_BYTE_INTEGRITY_UNVERIFIABLE")

    def test_symlink_artifact_and_private_manifest_denied(self):
        link = self.new_files / "symlink-private.bin"
        link.symlink_to(self.new_files / self.after_entries[0]["artifact"])
        self.after_entries[0]["artifact"] = "symlink-private.bin"
        self.write_manifests()
        self.denied("PRIVATE_SNAPSHOT_BYTE_INTEGRITY_UNVERIFIABLE")
        self.after_entries[0]["artifact"] = "renamed-identical.bin"
        self.write_manifests()
        alt = self.after / "aliased.json"
        alt.symlink_to(self.new_manifest)
        self.new_manifest = alt
        self.denied("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")

    def test_hardlink_private_file_denied(self):
        target = self.prev_files / self.before_entries[0]["artifact"]
        os.link(target, self.prev_files / "hardlink-identity.bin")
        self.denied("PRIVATE_SNAPSHOT_BYTE_INTEGRITY_UNVERIFIABLE")

    def test_unsafe_world_readable_manifest_and_root_denied(self):
        self.prev_manifest.chmod(0o644)
        self.denied("UNSAFE_PRIVATE_REQUEST_FILE")
        self.prev_manifest.chmod(0o600)
        self.prev_files.chmod(0o755)
        self.denied("PRIVATE_EVIDENCE_ROOT_NOT_OWNER_ONLY")

    def test_public_or_repeated_snapshot_roots_denied(self):
        self.report = self.repo / "public-result.json"
        self.denied("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
        self.report = self.report_dir / "delta.json"
        self.new_files = self.prev_files
        self.denied("INDEPENDENT_SNAPSHOT_ROOTS_REQUIRED")

    def test_stale_source_policy_denied(self):
        source = self.repo / "docs/governance/national-launch-blockers.json"
        parsed = json.loads(source.read_text())
        parsed["release_admission"] = "APPROVED"
        source.write_text(json.dumps(parsed))
        self.denied("SOURCE_INVENTORY_UNVERIFIABLE")

    def test_duplicate_manifest_key_denied(self):
        self.prev_manifest.write_text(
            '{"schema_version":1,"schema_version":1,"scope":"whatever","entries":[]}'
        )
        self.prev_manifest.chmod(0o600)
        self.denied("PRIVATE_REQUEST_UNVERIFIABLE")

    def test_output_preexists_is_never_replaced(self):
        self.report.write_text("CONFIDENTIAL_PREEXISTING")
        with self.assertRaises(batch.BatchError):
            self.compare()
        self.assertEqual(self.report.read_text(), "CONFIDENTIAL_PREEXISTING")

    def test_failed_write_cleans_only_newly_created_report(self):
        with patch.object(batch.os, "fsync", side_effect=OSError("sensitive disk")):
            self.denied("PRIVATE_OUTPUT_WRITE_FAILED")

    def test_validation_change_during_check_denied(self):
        original = reconcile.batch._read_request
        visits = {"count": 0}
        def switched(path, root):
            result = original(path, root)
            if path == self.new_manifest:
                visits["count"] += 1
                if visits["count"] == 2:
                    result = dict(result)
                    result["scope"] = "SWITCHED"
            return result
        with patch.object(reconcile.batch, "_read_request", side_effect=switched):
            self.denied("PRIVATE_SNAPSHOT_CHANGED_DURING_VALIDATION")

    def test_cli_prints_no_private_contents(self):
        process = subprocess.run([
            sys.executable, str(ROOT / "tools/policy_checks/"
                                "compare_private_evidence_snapshots.py"),
            "--root", str(self.repo),
            "--before-evidence-root", str(self.prev_files),
            "--before-manifest", str(self.prev_manifest),
            "--after-evidence-root", str(self.new_files),
            "--after-manifest", str(self.new_manifest),
            "--output-report", str(self.report),
        ], capture_output=True, text=True, check=False)
        self.assertEqual(process.returncode, 0, process.stderr)
        self.assertIn("release_allowed=false", process.stdout)
        for secret in ("private-operator-ref", "originalA",
                       "private-old-identity", str(self.private)):
            self.assertNotIn(secret, process.stdout + process.stderr)


if __name__ == "__main__":
    unittest.main()
