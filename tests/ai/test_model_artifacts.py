"""Synthetic filesystem tests for local model artifact preflight."""
import hashlib
import json
import io
from contextlib import redirect_stdout
import os
import tempfile
import unittest
from pathlib import Path

from ai.taranom_internal.model_artifacts import (
    ArtifactRejected, load_local_model_manifest, verify_local_model_artifact,
    verify_local_model_manifest,
)
from ai.taranom_internal.model_artifact_cli import main as artifact_cli_main


def manifest(name="weights.part", data=b"synthetic-model-bytes"):
    return {
        "schema_version": 1,
        "candidate_ref": "QWEN_QWEN2_5_7B_INSTRUCT",
        "artifact_version": "snapshot-test-1",
        "files": [{"path": name, "sha256": hashlib.sha256(data).hexdigest(), "size_bytes": len(data)}],
    }


class LocalModelArtifactTests(unittest.TestCase):
    def test_verifies_local_bytes_without_loading_or_granting_authority(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "weights.part").write_bytes(b"synthetic-model-bytes")
            result = verify_local_model_manifest(manifest(), root)
        self.assertEqual("LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED", result["status"])
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["license_reviewed"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["training_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_rejects_path_traversal_remote_refs_and_modified_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "weights.part").write_bytes(b"different-bytes")
            for changed, error in (
                (manifest("../weights.part"), "SAFE_RELATIVE_ARTIFACT_PATH"),
                ({**manifest(), "candidate_ref": "https://huggingface.co/model"}, "UNAPPROVED_INTERNAL_CANDIDATE"),
                (manifest(), "ARTIFACT_SIZE_MISMATCH"),
            ):
                with self.subTest(error=error), self.assertRaisesRegex(ArtifactRejected, error):
                    verify_local_model_manifest(changed, root)
            manifest_with_equal_size_change = manifest()
            (root / "weights.part").write_bytes(b"x" * manifest_with_equal_size_change["files"][0]["size_bytes"])
            with self.assertRaisesRegex(ArtifactRejected, "ARTIFACT_SHA256_MISMATCH"):
                verify_local_model_manifest(manifest_with_equal_size_change, root)

    def test_rejects_symlinks_duplicate_paths_and_missing_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "real.part").write_bytes(b"synthetic-model-bytes")
            (root / "weights.part").symlink_to(root / "real.part")
            with self.assertRaisesRegex(ArtifactRejected, "UNSAFE_ARTIFACT_PATH_COMPONENT"):
                verify_local_model_manifest(manifest(), root)
            duplicate = manifest()
            duplicate["files"] = duplicate["files"] * 2
            with self.assertRaisesRegex(ArtifactRejected, "DUPLICATE_ARTIFACT_PATH"):
                verify_local_model_manifest(duplicate, root)
            with self.assertRaisesRegex(ArtifactRejected, "LOCAL_ARTIFACT_FILE_MISSING"):
                verify_local_model_manifest(manifest("missing.part"), root)

    def test_rejects_invalid_digest_and_empty_manifest(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            bad = manifest()
            bad["files"][0]["sha256"] = "A" * 64
            with self.assertRaisesRegex(ArtifactRejected, "LOWERCASE_SHA256"):
                verify_local_model_manifest(bad, root)
            with self.assertRaisesRegex(ArtifactRejected, "BOUNDED_NONEMPTY_ARTIFACT"):
                verify_local_model_manifest({**manifest(), "files": []}, root)

    def test_rejects_absolute_windows_and_url_like_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            for name in ("/tmp/weights.part", "C:\\models\\weights.part", "https://host/model"):
                with self.subTest(name=name), self.assertRaisesRegex(
                        ArtifactRejected, "SAFE_RELATIVE_ARTIFACT_PATH"):
                    verify_local_model_manifest(manifest(name), Path(temp))


    def test_rejects_ambiguous_paths_and_special_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ("weights//part", "weights/./part", "weights/../part"):
                with self.subTest(name=name), self.assertRaisesRegex(
                        ArtifactRejected, "SAFE_RELATIVE_ARTIFACT_PATH"):
                    verify_local_model_manifest(manifest(name), root)
            if hasattr(os, "mkfifo"):
                os.mkfifo(root / "weights.part")
                with self.assertRaisesRegex(
                        ArtifactRejected, "LOCAL_REGULAR_ARTIFACT_FILE_REQUIRED"):
                    verify_local_model_manifest(manifest(), root)

    def test_rejects_symlinked_parent_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            outside = root / "outside"
            outside.mkdir()
            (outside / "weights.part").write_bytes(b"synthetic-model-bytes")
            (root / "nested").symlink_to(outside, target_is_directory=True)
            with self.assertRaisesRegex(
                    ArtifactRejected, "UNSAFE_ARTIFACT_PATH_COMPONENT"):
                verify_local_model_manifest(manifest("nested/weights.part"), root)

    def test_strict_local_manifest_cli_verifies_only_local_synthetic_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact_root = root / "weights"
            artifact_root.mkdir()
            data = b"synthetic-model-bytes"
            (artifact_root / "weights.part").write_bytes(data)
            manifest_path = root / "manifest.json"
            manifest_bytes = json.dumps(manifest()).encode("utf-8")
            manifest_path.write_bytes(manifest_bytes)

            output = io.StringIO()
            with redirect_stdout(output):
                status = artifact_cli_main([
                    "--manifest", str(manifest_path),
                    "--artifact-root", str(artifact_root),
                ])
            result = json.loads(output.getvalue())

        self.assertEqual(0, status)
        self.assertEqual("LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED", result["status"])
        self.assertEqual(hashlib.sha256(manifest_bytes).hexdigest(),
                         result["manifest_sha256"])
        self.assertFalse(result["manifest_authenticity_verified"])
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["license_reviewed"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_manifest_loader_rejects_duplicate_keys_nonfinite_json_and_invalid_utf8(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "manifest.json"
            for content, reason in (
                ('{"schema_version":1,"schema_version":1}', "DUPLICATE_MODEL_MANIFEST_KEY"),
                ('{"schema_version":NaN}', "NONFINITE_MODEL_MANIFEST_NUMBER_DENIED"),
                ("[" * 1100 + "]" * 1100, "MODEL_MANIFEST_NESTING_LIMIT"),
            ):
                path.write_text(content, encoding="utf-8")
                with self.subTest(reason=reason, prefix=content[:24]), self.assertRaisesRegex(
                        ArtifactRejected, reason):
                    load_local_model_manifest(path)
            path.write_bytes(b"\xff")
            with self.assertRaisesRegex(
                    ArtifactRejected, "STRICT_MODEL_MANIFEST_JSON_REQUIRED"):
                load_local_model_manifest(path)

    def test_manifest_loader_rejects_symlinks_special_files_and_oversize(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / "target.json"
            target.write_text(json.dumps(manifest()), encoding="utf-8")
            link = root / "link.json"
            link.symlink_to(target)
            with self.assertRaisesRegex(ArtifactRejected, "SAFE_LOCAL_MANIFEST_OPEN_FAILED"):
                load_local_model_manifest(link)

            oversized = root / "oversized.json"
            oversized.write_bytes(b" " * (1024 * 1024 + 1))
            with self.assertRaisesRegex(ArtifactRejected, "MODEL_MANIFEST_SIZE_LIMIT"):
                load_local_model_manifest(oversized)

    def test_loaded_manifest_drives_existing_artifact_byte_verification(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            result = verify_local_model_artifact(manifest_path, root)
            manifest_bytes = manifest_path.read_bytes()
        self.assertEqual("LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED", result["status"])
        self.assertEqual(hashlib.sha256(manifest_bytes).hexdigest(),
                         result["manifest_sha256"])
        self.assertFalse(result["manifest_authenticity_verified"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["clinical_use_allowed"])


if __name__ == "__main__":
    unittest.main()
