"""Regression for PRIVATE external evidence custody; no real external evidence."""
import hashlib
import json
import os
import subprocess
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "policy_checks"))
import check_private_external_evidence as custody  # noqa: E402
from check_private_external_evidence import inspect, SCOPE  # noqa: E402


class EvidenceCustodyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.top = Path(self.temp.name)
        self.repo = self.top / "public-repo"
        dest = self.repo / "docs" / "governance"
        dest.mkdir(parents=True)
        shutil.copy2(ROOT / "docs/governance/national-launch-blockers.json",
                     dest / "national-launch-blockers.json")
        self.private = self.top / "private"
        self.private.mkdir()
        self.payload = self.private / "legal-proof.pdf"
        self.payload.write_bytes(b"external evidence example; no genuine authority")
        self.manifest = self.top / "private-manifest.json"
        self.inventory_hash = hashlib.sha256(
            (dest / "national-launch-blockers.json").read_bytes()).hexdigest()
        self.entry = {
            "dimension": "TRN-EP-01",
            "blocker": "VERIFIED_MEMBER_IDENTITY",
            "artifact": "legal-proof.pdf",
            "sha256": hashlib.sha256(self.payload.read_bytes()).hexdigest(),
            "custodian_ref": "local-custodian-ref",
            "review_state": "UNREVIEWED",
        }
        self.write([self.entry])

    def write(self, entries, **overrides):
        obj = {"schema_version": 1, "scope": SCOPE,
               "source_inventory_sha256": self.inventory_hash, "entries": entries}
        obj.update(overrides)
        self.manifest.write_text(json.dumps(obj), encoding="utf-8")

    def check(self):
        return inspect(self.repo, self.private, self.manifest)

    def assert_denied(self, reason):
        result = self.check()
        self.assertEqual(result["status"], "DENIED_INVALID_INTAKE")
        self.assertIn(reason, result["errors"])
        self.assertFalse(result["release_allowed"])
        self.assertFalse(result["external_evidence_verified"])
        self.assertFalse(result["independent_review_observed"])
        self.assertNotIn("legal-proof.pdf", json.dumps(result))
        return result

    def test_actual_bytes_integrity_is_not_approval(self):
        result = self.check()
        self.assertEqual(result["status"], "INTACT_FILES_UNREVIEWED")
        self.assertEqual(result["submitted_files_integrity_checked"], 1)
        self.assertEqual(result["distinct_blocker_slots_with_files"], 1)
        self.assertFalse(result["release_allowed"])
        self.assertFalse(result["external_evidence_verified"])

    def test_empty_public_template_does_not_certify(self):
        self.write([], source_inventory_sha256=None)
        self.manifest = self.repo / "docs/governance/template.json"
        self.write([], source_inventory_sha256=None)
        result = self.check()
        self.assertEqual(result["status"], "EMPTY_TEMPLATE_NO_EVIDENCE")
        self.assertEqual(result["submitted_files_integrity_checked"], 0)

    def test_modified_bytes_rejected(self):
        self.payload.write_bytes(b"changed data")
        self.assert_denied("ARTIFACT_SHA256_MISMATCH")

    def test_empty_bytes_rejected(self):
        self.payload.write_bytes(b"")
        self.assert_denied("ARTIFACT_SIZE_UNVERIFIABLE")

    def test_missing_file_rejected(self):
        self.payload.unlink()
        self.assert_denied("ARTIFACT_UNAVAILABLE")

    def test_mismatched_source_inventory(self):
        self.write([self.entry], source_inventory_sha256="a"*64)
        self.assert_denied("SOURCE_INVENTORY_DIGEST_MISMATCH")

    def test_unknown_dimension(self):
        self.write([dict(self.entry, dimension="TRN-EP-99")])
        self.assert_denied("UNKNOWN_LAUNCH_DIMENSION")

    def test_unknown_blocker(self):
        self.write([dict(self.entry, blocker="PRODUCTION_HOSTING_AND_SECURITY")])
        self.assert_denied("UNMAPPED_EXTERNAL_BLOCKER")

    def test_cannot_self_certify_human_approval(self):
        self.write([dict(self.entry, review_state="APPROVED")])
        self.assert_denied("SELF_APPROVAL_FORBIDDEN")

    def test_duplicate_source_records(self):
        self.write([self.entry, self.entry])
        self.assert_denied("DUPLICATE_EVIDENCE_RECORD")

    def test_path_escape_rejected(self):
        self.write([dict(self.entry, artifact="../private/legal-proof.pdf")])
        self.assert_denied("UNSAFE_ARTIFACT_REFERENCE")

    def test_absolute_path_rejected(self):
        self.write([dict(self.entry, artifact=str(self.payload))])
        self.assert_denied("UNSAFE_ARTIFACT_REFERENCE")

    def test_symlinked_artifact_rejected(self):
        (self.private / "shortcut.pdf").symlink_to(self.payload)
        self.write([dict(self.entry, artifact="shortcut.pdf")])
        self.assert_denied("SYMLINKED_ARTIFACT_FORBIDDEN")

    def test_public_manifest_with_real_entries_rejected(self):
        self.manifest = self.repo / "docs/governance/private-leak.json"
        self.write([self.entry])
        self.assert_denied("PRIVATE_MANIFEST_INSIDE_PUBLIC_REPO")

    def test_public_evidence_root_rejected(self):
        self.assertEqual(inspect(self.repo, self.repo, self.manifest)["errors"],
                         ["PRIVATE_EVIDENCE_ROOT_INSIDE_PUBLIC_REPO"])

    def test_bad_hex_digest_rejected(self):
        self.write([dict(self.entry, sha256="ABC")])
        self.assert_denied("INVALID_ARTIFACT_DIGEST")

    def test_schema_does_not_allow_ready_or_legal_claims(self):
        self.write([self.entry], release_allowed=True)
        self.assert_denied("INTAKE_SCHEMA_INVALID")

    def test_schema_does_not_allow_unknown_evidence_fields(self):
        self.write([dict(self.entry, medical_license_approved=True)])
        self.assert_denied("EVIDENCE_RECORD_SCHEMA_INVALID")

    def test_duplicate_json_key_rejected(self):
        self.manifest.write_text('{"schema_version":1,"schema_version":1}', encoding="utf-8")
        self.assert_denied("DUPLICATE_JSON_KEY")

    def test_source_inventory_must_stay_denied(self):
        path = self.repo / "docs/governance/national-launch-blockers.json"
        data = json.loads(path.read_text())
        data["release_admission"] = "READY"
        path.write_text(json.dumps(data))
        self.assert_denied("SOURCE_BLOCKER_INVENTORY_INVALID")

    def test_changed_source_manifest_rejected_even_if_valid_structure(self):
        path = self.repo / "docs/governance/national-launch-blockers.json"
        data = json.loads(path.read_text())
        data["statement"] += " altered"
        path.write_text(json.dumps(data))
        self.assert_denied("SOURCE_INVENTORY_DIGEST_MISMATCH")

    def test_wrong_custodian_ref_rejected(self):
        self.write([dict(self.entry, custodian_ref="a")])
        self.assert_denied("INVALID_OPAQUE_CUSTODIAN_REF")

    def test_two_distinct_artifacts_one_blocker_slot(self):
        more = self.private / "second.bin"
        more.write_bytes(b"another illustrative file")
        extra = dict(self.entry, artifact="second.bin",
                     sha256=hashlib.sha256(more.read_bytes()).hexdigest())
        self.write([self.entry, extra])
        result = self.check()
        self.assertEqual(result["submitted_files_integrity_checked"], 2)
        self.assertEqual(result["distinct_blocker_slots_with_files"], 1)
        self.assertFalse(result["release_allowed"])

    def test_nothing_sensitive_in_error_output(self):
        self.write([dict(self.entry, artifact="../secret-patient-report.pdf")])
        result = self.assert_denied("UNSAFE_ARTIFACT_REFERENCE")
        self.assertNotIn("patient", json.dumps(result))
        self.assertNotIn(self.entry["sha256"], json.dumps(result))

    def test_nonfinite_json_rejected(self):
        self.manifest.write_text('{"oops":NaN}', encoding="utf-8")
        self.assert_denied("NONFINITE_JSON")


    def test_nested_real_private_artifact_is_accepted_without_approval(self):
        folder = self.private / "nested"
        folder.mkdir()
        target = folder / self.payload.name
        self.payload.replace(target)
        self.write([dict(self.entry, artifact="nested/legal-proof.pdf")])
        result = self.check()
        self.assertEqual(result["status"], "INTACT_FILES_UNREVIEWED")
        self.assertFalse(result["release_allowed"])

    def test_intermediate_directory_symlink_rejected(self):
        nested = self.private / "original"
        nested.mkdir()
        (nested / "legal-proof.pdf").write_bytes(self.payload.read_bytes())
        (self.private / "alias").symlink_to(nested, target_is_directory=True)
        self.write([dict(self.entry, artifact="alias/legal-proof.pdf")])
        self.assert_denied("SYMLINKED_ARTIFACT_FORBIDDEN")

    def test_symlinked_private_root_rejected(self):
        alias = self.top / "private-alias"
        alias.symlink_to(self.private, target_is_directory=True)
        result = inspect(self.repo, alias, self.manifest)
        self.assertIn("PRIVATE_EVIDENCE_ROOT_UNAVAILABLE", result["errors"])

    def test_hardlinked_artifact_rejected(self):
        linked = self.private / "linked.pdf"
        os.link(self.payload, linked)
        self.write([dict(self.entry, artifact="linked.pdf")])
        self.assert_denied("HARDLINKED_ARTIFACT_FORBIDDEN")

    def test_replaced_path_during_read_is_rejected(self):
        # Deterministically replace the directory entry while the opened
        # descriptor continues to read the original bytes. A hash alone
        # cannot identify this post-open pathname replacement.
        original_fdopen = os.fdopen
        payload = self.payload
        swapped = []

        class SwappingStream:
            def __init__(self, fd, mode):
                self.stream = original_fdopen(fd, mode)

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return self.stream.__exit__(*args)

            def read(self, size):
                chunk = self.stream.read(size)
                if chunk and not swapped:
                    swapped.append(True)
                    payload.rename(payload.parent / "moved.pdf")
                    payload.write_bytes(chunk)
                return chunk

        with patch.object(custody.os, "fdopen", side_effect=SwappingStream):
            self.assert_denied("ARTIFACT_PATH_CHANGED")

    def test_private_manifest_alias_inside_public_repo_rejected(self):
        public_alias = self.repo / "private-alias.json"
        public_alias.symlink_to(self.manifest)
        result = inspect(self.repo, self.private, public_alias)
        self.assertIn("PRIVATE_MANIFEST_INSIDE_PUBLIC_REPO", result["errors"])

    def test_private_report_must_not_be_written_into_public_tree(self):
        leaked_report = self.repo / "would-leak.json"
        proc = subprocess.run(
            [sys.executable, str(ROOT / "tools/policy_checks/check_private_external_evidence.py"),
             "--root", str(self.repo), "--evidence-root", str(self.private),
             "--manifest", str(self.manifest), "--report-json", str(leaked_report)],
            capture_output=True, text=True, check=False)
        self.assertNotEqual(proc.returncode, 0)
        self.assertIn("PRIVATE_REPORT_OUTSIDE_PUBLIC_REPO_REQUIRED", proc.stderr)
        self.assertFalse(leaked_report.exists())

    def test_real_private_cli_report_stays_redacted_and_denied(self):
        out = self.top / "private-report.json"
        proc = subprocess.run(
            [sys.executable, str(ROOT / "tools/policy_checks/check_private_external_evidence.py"),
             "--root", str(self.repo), "--evidence-root", str(self.private),
             "--manifest", str(self.manifest), "--report-json", str(out)],
            capture_output=True, text=True, check=False)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        contents = out.read_text()
        self.assertNotIn(self.entry["artifact"], contents)
        self.assertNotIn(self.entry["sha256"], contents)
        self.assertNotIn(self.entry["custodian_ref"], contents)
        self.assertFalse(json.loads(contents)["release_allowed"])


if __name__ == "__main__":
    unittest.main()
