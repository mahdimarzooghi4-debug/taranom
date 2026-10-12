"""TRN-TECH-079: offline real private bytes; no real partner information or network."""
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
import build_private_evidence_batch as batch
import check_private_external_evidence as custody


class PrivateEvidenceBatchTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.home = Path(self.tmp.name)
        self.repo = self.home / "public-repo"
        dest = self.repo / "docs" / "governance"
        dest.mkdir(parents=True)
        shutil.copy2(ROOT / "docs/governance/national-launch-blockers.json",
                     dest / "national-launch-blockers.json")
        self.private = self.home / "private"
        self.private.mkdir(mode=0o700)
        self.evidence = self.private / "evidence"
        self.evidence.mkdir(mode=0o700)
        self.secret_file = self.evidence / "secret-vendor-attestation.bin"
        self.secret_file.write_bytes(b"private illustrative evidence only")
        self.request = self.private / "request.json"
        self.manifest = self.private / "generated.json"
        self.report = self.private / "gap.json"
        self.rows = [
            self.row("TRN-EP-01", "VERIFIED_MEMBER_IDENTITY"),
            self.row("MARKET-SUPPORT", "ALL_3_MARKET_OPERATIONS"),
            self.row("MARKET-CORPORATE", "DATA_RIGHTS_AND_CONSENT"),
            self.row("NATIONAL-COVERAGE", "NATIONAL_COVERAGE_EVIDENCE"),
        ]
        self.write_request(self.rows)

    def row(self, dimension, blocker):
        return {"dimension": dimension, "blocker": blocker,
                "artifact": "secret-vendor-attestation.bin",
                "custodian_ref": "private-custodian-ref"}

    def write_request(self, rows, **changes):
        data = {"schema_version": 1, "scope": batch.REQUEST_SCOPE,
                "entries": rows}
        data.update(changes)
        self.request.write_text(json.dumps(data), encoding="utf-8")
        self.request.chmod(0o600)

    def build(self):
        return batch.build(self.repo, self.evidence, self.request,
                           self.manifest, self.report)

    def denied(self, reason):
        with self.assertRaises(batch.BatchError) as caught:
            self.build()
        self.assertEqual(str(caught.exception), reason)
        self.assertFalse(self.manifest.exists())
        self.assertFalse(self.report.exists())

    def test_real_bytes_generate_checked_private_manifest_and_17_gap_dimensions(self):
        result = self.build()
        self.assertEqual(result["status"], "UNREVIEWED_FILES_INTACT_NOT_APPROVED")
        self.assertEqual(result["dimension_count"], 17)
        self.assertEqual(result["blocker_slots_with_unreviewed_files"], 4)
        self.assertFalse(result["external_evidence_verified"])
        self.assertFalse(result["national_release_allowed"])
        self.assertIsNone(result["overall_product_completion_percentage"])
        self.assertEqual(len(result["dimensions"]), 17)
        self.assertTrue(all(d["status"] == "BLOCKED_UNVERIFIED"
                            for d in result["dimensions"]))
        saved = json.loads(self.manifest.read_text())
        self.assertEqual(len(saved["entries"]), 4)
        self.assertTrue(all(x["review_state"] == "UNREVIEWED"
                            for x in saved["entries"]))
        self.assertTrue(all(x["sha256"] == hashlib.sha256(
            self.secret_file.read_bytes()).hexdigest() for x in saved["entries"]))
        self.assertEqual(custody.inspect(self.repo, self.evidence, self.manifest)
                         ["status"], "INTACT_FILES_UNREVIEWED")
        self.assertEqual(stat.S_IMODE(self.manifest.stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(self.report.stat().st_mode), 0o600)
        public_readable = self.report.read_text()
        for forbidden in ("secret-vendor-attestation", "private-custodian-ref",
                          saved["entries"][0]["sha256"]):
            self.assertNotIn(forbidden, public_readable)
        self.assertEqual(json.loads(public_readable), result)

    def test_unknown_blocker_and_dimension_fail_closed(self):
        self.write_request([self.row("TRN-EP-01", "FAKE_APPROVAL")])
        self.denied("UNRECOGNIZED_RELEASE_BLOCKER")
        self.write_request([self.row("TRN-EP-99", "VERIFIED_MEMBER_IDENTITY")])
        self.denied("UNRECOGNIZED_RELEASE_DIMENSION")

    def test_request_cannot_self_approve_or_claim_real_rights(self):
        row = {**self.rows[0], "review_state": "APPROVED"}
        self.write_request([row])
        self.denied("INVALID_PRIVATE_REQUEST_ENTRY")
        self.write_request(self.rows, national_release_allowed=True)
        self.denied("INVALID_PRIVATE_REQUEST_SCHEMA")

    def test_duplicate_slot_artifact_denied(self):
        self.write_request([self.rows[0], self.rows[0]])
        self.denied("DUPLICATE_PRIVATE_REQUEST_ENTRY")

    def test_private_request_must_be_owner_only(self):
        self.request.chmod(0o644)
        self.denied("UNSAFE_PRIVATE_REQUEST_FILE")

    def test_private_request_symlink_and_hardlink_denied(self):
        alternative = self.private / "alias.json"
        alternative.symlink_to(self.request)
        self.request = alternative
        self.denied("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
        self.request = self.private / "request.json"
        hard = self.private / "hardlink.json"
        os.link(self.request, hard)
        self.denied("UNSAFE_PRIVATE_REQUEST_FILE")

    def test_private_paths_must_not_be_under_public_git(self):
        self.request = self.repo / "private-request.json"
        self.write_request(self.rows)
        self.denied("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
        self.request = self.private / "request.json"
        self.write_request(self.rows)
        self.manifest = self.repo / "leak.json"
        self.denied("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")
        self.manifest = self.private / "generated.json"
        self.report = self.repo / "report-leak.json"
        self.denied("PRIVATE_PATH_OUTSIDE_PUBLIC_REPOSITORY_REQUIRED")

    def test_artifact_traversal_and_symlink_denied(self):
        self.write_request([{**self.rows[0], "artifact": "../request.json"}])
        self.denied("UNSAFE_EVIDENCE_ARTIFACT_NAME")
        link = self.evidence / "symlink.bin"
        link.symlink_to(self.secret_file)
        self.write_request([{**self.rows[0], "artifact": "symlink.bin"}])
        self.denied("PRIVATE_EVIDENCE_BYTE_INTEGRITY_UNVERIFIABLE")

    def test_original_file_digest_checked_and_source_version_pinned(self):
        self.build()
        saved = json.loads(self.manifest.read_text())
        self.assertEqual(saved["source_inventory_sha256"], hashlib.sha256(
            (self.repo / "docs/governance/national-launch-blockers.json")
            .read_bytes()).hexdigest())
        self.secret_file.write_bytes(b"mutated")
        self.assertEqual(custody.inspect(self.repo, self.evidence, self.manifest)
                         ["status"], "DENIED_INVALID_INTAKE")

    def test_invalid_inventory_denied_without_outputs(self):
        inventory = self.repo / "docs/governance/national-launch-blockers.json"
        source = json.loads(inventory.read_text())
        source["release_admission"] = "ALLOWED"
        inventory.write_text(json.dumps(source))
        self.denied("SOURCE_INVENTORY_UNVERIFIABLE")

    def test_output_is_no_clobber(self):
        self.manifest.write_bytes(b"existing-private-evidence")
        with self.assertRaises(batch.BatchError):
            self.build()
        self.assertEqual(self.manifest.read_bytes(), b"existing-private-evidence")
        self.assertFalse(self.report.exists())

    def test_unsafe_output_parent_denied(self):
        self.private.chmod(0o755)
        self.denied("UNSAFE_PRIVATE_OUTPUT_DIRECTORY")

    def test_integrity_recheck_failure_cleans_created_manifest(self):
        with patch.object(batch.custody, "inspect",
                          return_value={"status": "DENIED_INVALID_INTAKE",
                                        "submitted_files_integrity_checked": 0}):
            self.denied("POST_WRITE_INTEGRITY_RECHECK_FAILED")

    def test_private_cli_does_not_print_private_values(self):
        process = subprocess.run(
            [sys.executable, str(ROOT / "tools/policy_checks/"
                                 "build_private_evidence_batch.py"),
             "--root", str(self.repo), "--evidence-root", str(self.evidence),
             "--request-file", str(self.request),
             "--output-manifest", str(self.manifest),
             "--output-report", str(self.report)],
            capture_output=True, text=True, check=False)
        self.assertEqual(process.returncode, 0, process.stderr)
        self.assertIn("release_allowed=false", process.stdout)
        for secret in ("secret-vendor-attestation", "private-custodian-ref"):
            self.assertNotIn(secret, process.stdout + process.stderr)
        self.assertFalse(json.loads(self.report.read_text())["release_allowed"])


if __name__ == "__main__":
    unittest.main()
