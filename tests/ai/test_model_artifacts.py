"""Synthetic filesystem tests for local model artifact preflight."""
import hashlib
import json
import io
from contextlib import redirect_stdout
import os
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

from ai.taranom_internal.model_artifacts import (
    ArtifactRejected, load_local_model_manifest,
    store_verified_local_model_artifact, verify_local_model_store_entry,
    list_local_model_store_entries, verify_local_model_artifact,
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
        self.assertEqual([{
            "path": "weights.part",
            "sha256": hashlib.sha256(b"synthetic-model-bytes").hexdigest(),
            "size_bytes": len(b"synthetic-model-bytes"),
        }], result["verified_files"])
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

    def test_local_store_publishes_verified_copy_atomically(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            store = root / "managed-store"
            store.mkdir()
            data = b"synthetic-model-bytes"
            (source / "weights.part").write_bytes(data)
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")

            result = store_verified_local_model_artifact(
                manifest_path, source, store
            )
            target = store / result["store_ref"]

            self.assertEqual("LOCAL_ARTIFACT_STORED_NOT_LOADED", result["status"])
            self.assertTrue(result["store_created"])
            self.assertEqual(data, (target / "payload" / "weights.part").read_bytes())
            self.assertEqual(
                manifest_path.read_bytes(), (target / "manifest.json").read_bytes()
            )
            receipt = json.loads((target / "verification.json").read_text("utf-8"))
            self.assertEqual(result["store_ref"], receipt["store_ref"])
            self.assertEqual(
                result["store_ref"],
                receipt["copy_verification"]["artifact_set_sha256"],
            )
            self.assertEqual(
                result["verification"]["verified_files"],
                receipt["copy_verification"]["verified_files"],
            )
            self.assertFalse(receipt["copy_verification"]["model_loaded"])
            self.assertFalse(receipt["model_loaded"])
            self.assertFalse(receipt["manifest_authenticity_verified"])
            self.assertFalse(receipt["clinical_use_allowed"])
            self.assertFalse(receipt["production_allowed"])
            readback = verify_local_model_store_entry(store, result["store_ref"])
            self.assertEqual(
                "LOCAL_ARTIFACT_STORE_ENTRY_VERIFIED_NOT_LOADED",
                readback["status"],
            )
            self.assertTrue(readback["stored_content_verified"])
            self.assertTrue(readback["receipt_consistency_verified"])
            self.assertEqual(
                result["store_ref"],
                readback["copy_verification"]["artifact_set_sha256"],
            )
            self.assertEqual(
                {result["store_ref"]}, {entry.name for entry in store.iterdir()}
            )
            with self.assertRaisesRegex(
                    ArtifactRejected, "ARTIFACT_STORE_TARGET_ALREADY_EXISTS"):
                store_verified_local_model_artifact(manifest_path, source, store)

    def test_local_store_rejects_symlinked_store_root(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            real_store = root / "real-store"
            real_store.mkdir()
            linked_store = root / "linked-store"
            linked_store.symlink_to(real_store, target_is_directory=True)

            with self.assertRaisesRegex(
                    ArtifactRejected, "EXISTING_LOCAL_ARTIFACT_STORE_ROOT_REQUIRED"):
                store_verified_local_model_artifact(
                    manifest_path, source, linked_store
                )

    def test_expected_manifest_and_artifact_set_pins_match_or_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact_root = root / "artifacts"
            artifact_root.mkdir()
            data = b"synthetic-model-bytes"
            (artifact_root / "weights.part").write_bytes(data)
            manifest_path = root / "manifest.json"
            raw = json.dumps(manifest()).encode("utf-8")
            manifest_path.write_bytes(raw)

            baseline = verify_local_model_artifact(manifest_path, artifact_root)
            verified = verify_local_model_artifact(
                manifest_path,
                artifact_root,
                expected_manifest_sha256=baseline["manifest_sha256"],
                expected_artifact_set_sha256=baseline["artifact_set_sha256"],
            )
            self.assertTrue(verified["manifest_pin_supplied"])
            self.assertTrue(verified["artifact_set_pin_supplied"])

            with self.assertRaisesRegex(ArtifactRejected, "MODEL_MANIFEST_PIN_MISMATCH"):
                verify_local_model_artifact(
                    manifest_path, artifact_root, expected_manifest_sha256="0" * 64
                )
            with self.assertRaisesRegex(ArtifactRejected, "ARTIFACT_SET_PIN_MISMATCH"):
                verify_local_model_artifact(
                    manifest_path,
                    artifact_root,
                    expected_artifact_set_sha256="0" * 64,
                )
            with self.assertRaisesRegex(
                    ArtifactRejected, "LOWERCASE_MODEL_MANIFEST_SHA256_PIN_REQUIRED"):
                verify_local_model_artifact(
                    manifest_path, artifact_root, expected_manifest_sha256="not-a-digest"
                )

    def test_artifact_set_fingerprint_is_order_independent_and_versioned(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            first_bytes = b"synthetic-first"
            second_bytes = b"synthetic-second"
            (root / "first.part").write_bytes(first_bytes)
            (root / "second.part").write_bytes(second_bytes)
            first_entry = {
                "path": "first.part",
                "sha256": hashlib.sha256(first_bytes).hexdigest(),
                "size_bytes": len(first_bytes),
            }
            second_entry = {
                "path": "second.part",
                "sha256": hashlib.sha256(second_bytes).hexdigest(),
                "size_bytes": len(second_bytes),
            }
            base_manifest = {
                "schema_version": 1,
                "candidate_ref": "QWEN_QWEN2_5_7B_INSTRUCT",
                "artifact_version": "snapshot-test-1",
                "files": [first_entry, second_entry],
            }
            forward = verify_local_model_manifest(base_manifest, root)
            reverse = verify_local_model_manifest({
                **base_manifest, "files": [second_entry, first_entry],
            }, root)
            next_version = verify_local_model_manifest({
                **base_manifest, "artifact_version": "snapshot-test-2",
            }, root)

        self.assertRegex(forward["artifact_set_sha256"], r"^[0-9a-f]{64}$")
        self.assertEqual(
            forward["artifact_set_sha256"], reverse["artifact_set_sha256"]
        )
        self.assertNotEqual(
            forward["artifact_set_sha256"], next_version["artifact_set_sha256"]
        )

    def test_rejects_undeclared_files_and_unsafe_directory_entries(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "weights.part").write_bytes(b"synthetic-model-bytes")
            (root / "unexpected.part").write_bytes(b"unlisted")
            with self.assertRaisesRegex(
                    ArtifactRejected, "UNDECLARED_LOCAL_ARTIFACT_FILE"):
                verify_local_model_manifest(manifest(), root)
            (root / "unexpected.part").unlink()

            nested = root / "nested"
            nested.mkdir()
            (nested / "hidden.part").write_bytes(b"unlisted")
            with self.assertRaisesRegex(
                    ArtifactRejected, "UNDECLARED_LOCAL_ARTIFACT_FILE"):
                verify_local_model_manifest(manifest(), root)
            (nested / "hidden.part").unlink()
            nested.rmdir()

            if hasattr(os, "mkfifo"):
                os.mkfifo(root / "unexpected.pipe")
                with self.assertRaisesRegex(
                        ArtifactRejected, "UNSAFE_ARTIFACT_DIRECTORY_ENTRY"):
                    verify_local_model_manifest(manifest(), root)


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

            expected_artifact_set = verify_local_model_manifest(
                manifest(), artifact_root
            )["artifact_set_sha256"]
            output = io.StringIO()
            with redirect_stdout(output):
                status = artifact_cli_main([
                    "--manifest", str(manifest_path),
                    "--artifact-root", str(artifact_root),
                    "--expected-manifest-sha256",
                    hashlib.sha256(manifest_bytes).hexdigest(),
                    "--expected-artifact-set-sha256", expected_artifact_set,
                ])
            result = json.loads(output.getvalue())

        self.assertEqual(0, status)
        self.assertEqual("LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED", result["status"])
        self.assertEqual(hashlib.sha256(manifest_bytes).hexdigest(),
                         result["manifest_sha256"])
        self.assertRegex(result["artifact_set_sha256"], r"^[0-9a-f]{64}$")
        self.assertTrue(result["manifest_pin_supplied"])
        self.assertTrue(result["artifact_set_pin_supplied"])
        self.assertFalse(result["manifest_authenticity_verified"])
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["license_reviewed"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_cli_stores_verified_package_when_explicit_store_root_is_given(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact_root = root / "weights"
            artifact_root.mkdir()
            data = b"synthetic-model-bytes"
            (artifact_root / "weights.part").write_bytes(data)
            manifest_path = root / "manifest.json"
            manifest_bytes = json.dumps(manifest()).encode("utf-8")
            manifest_path.write_bytes(manifest_bytes)
            store_root = root / "managed-store"
            store_root.mkdir()

            output = io.StringIO()
            with redirect_stdout(output):
                status = artifact_cli_main([
                    "--manifest", str(manifest_path),
                    "--artifact-root", str(artifact_root),
                    "--store-root", str(store_root),
                ])
            result = json.loads(output.getvalue())
            stored = store_root / result["store_ref"]
            stored_payload = (stored / "payload" / "weights.part").read_bytes()
            stored_manifest = (stored / "manifest.json").read_bytes()

        self.assertEqual(0, status)
        self.assertEqual("LOCAL_ARTIFACT_STORED_NOT_LOADED", result["status"])
        self.assertTrue(result["store_created"])
        self.assertEqual(data, stored_payload)
        self.assertEqual(manifest_bytes, stored_manifest)
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_cli_verifies_stored_package_read_only(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact_root = root / "weights"
            artifact_root.mkdir()
            (artifact_root / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store_root = root / "managed-store"
            store_root.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, artifact_root, store_root
            )

            output = io.StringIO()
            with redirect_stdout(output):
                status = artifact_cli_main([
                    "--store-root", str(store_root),
                    "--verify-store-ref", created["store_ref"],
                ])
            result = json.loads(output.getvalue())

        self.assertEqual(0, status)
        self.assertEqual(
            "LOCAL_ARTIFACT_STORE_ENTRY_VERIFIED_NOT_LOADED", result["status"]
        )
        self.assertTrue(result["receipt_consistency_verified"])
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["production_allowed"])

    def test_cli_rejects_mismatched_manifest_pin(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            artifact_root = root / "weights"
            artifact_root.mkdir()
            (artifact_root / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")

            output = io.StringIO()
            with redirect_stdout(output):
                status = artifact_cli_main([
                    "--manifest", str(manifest_path),
                    "--artifact-root", str(artifact_root),
                    "--expected-manifest-sha256", "0" * 64,
                ])
            result = json.loads(output.getvalue())

        self.assertEqual(2, status)
        self.assertEqual("DENIED", result["status"])
        self.assertEqual("MODEL_MANIFEST_PIN_MISMATCH", result["error"])
        self.assertTrue(result["manifest_pin_supplied"])
        self.assertFalse(result["artifact_set_pin_supplied"])
        self.assertFalse(result["manifest_authenticity_verified"])
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
            artifact_root = root / "artifacts"
            artifact_root.mkdir()
            (artifact_root / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            result = verify_local_model_artifact(manifest_path, artifact_root)
            manifest_bytes = manifest_path.read_bytes()
        self.assertEqual("LOCAL_ARTIFACT_BYTES_VERIFIED_NOT_LOADED", result["status"])
        self.assertEqual(hashlib.sha256(manifest_bytes).hexdigest(),
                         result["manifest_sha256"])
        self.assertFalse(result["manifest_authenticity_verified"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["clinical_use_allowed"])



    def test_readback_denies_same_size_payload_corruption(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )
            payload = store / created["store_ref"] / "payload" / "weights.part"
            payload.write_bytes(b"x" * len(b"synthetic-model-bytes"))

            with self.assertRaisesRegex(
                    ArtifactRejected, "ARTIFACT_SHA256_MISMATCH"):
                verify_local_model_store_entry(store, created["store_ref"])

    def test_readback_denies_tampered_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )
            receipt_path = store / created["store_ref"] / "verification.json"
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            receipt["status"] = "LOCAL_ARTIFACT_STORED_AND_APPROVED"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")

            with self.assertRaisesRegex(
                    ArtifactRejected, "ARTIFACT_STORE_RECEIPT_INVALID"):
                verify_local_model_store_entry(store, created["store_ref"])

    def test_readback_denies_extra_store_entries_and_symlinked_payload(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )
            package = store / created["store_ref"]
            (package / "unexpected.json").write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(
                    ArtifactRejected, "ARTIFACT_STORE_ENTRY_SCHEMA_INVALID"):
                verify_local_model_store_entry(store, created["store_ref"])
            (package / "unexpected.json").unlink()

            payload = package / "payload"
            payload.rename(package / "payload.real")
            payload.symlink_to(package / "payload.real", target_is_directory=True)
            with self.assertRaisesRegex(
                    ArtifactRejected, "UNSAFE_ARTIFACT_STORE_ENTRY"):
                verify_local_model_store_entry(store, created["store_ref"])


    def test_read_only_store_inventory_verifies_entries_and_reports_unknown_items(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )
            (store / "unexpected-entry").write_text("ignored?", encoding="utf-8")

            output = io.StringIO()
            with redirect_stdout(output):
                status = artifact_cli_main(["--store-root", str(store), "--list-store"])
            result = json.loads(output.getvalue())

        self.assertEqual(0, status)
        self.assertEqual("LOCAL_ARTIFACT_STORE_INVENTORY_READ_ONLY", result["status"])
        self.assertEqual(2, result["entry_count"])
        entries = {entry["store_ref"]: entry for entry in result["entries"]}
        self.assertEqual(
            "LOCAL_ARTIFACT_STORE_ENTRY_VERIFIED_NOT_LOADED",
            entries[created["store_ref"]]["status"],
        )
        self.assertTrue(entries[created["store_ref"]]["stored_content_verified"])
        self.assertTrue(entries[created["store_ref"]]["receipt_consistency_verified"])
        self.assertEqual("DENIED", entries["unexpected-entry"]["status"])
        self.assertFalse(entries["unexpected-entry"]["stored_content_verified"])
        self.assertFalse(result["model_loaded"])
        self.assertFalse(result["network_used"])
        self.assertFalse(result["manifest_authenticity_verified"])
        self.assertFalse(result["license_reviewed"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["training_allowed"])
        self.assertFalse(result["production_allowed"])


    def test_store_readback_hashes_payload_once_and_reconstructs_copy_pins(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )

            with patch(
                    "ai.taranom_internal.model_artifacts.verify_local_model_manifest",
                    wraps=verify_local_model_manifest,
            ) as verify:
                readback = verify_local_model_store_entry(
                    store, created["store_ref"]
                )

        self.assertEqual(1, verify.call_count)
        self.assertTrue(readback["stored_content_verified"])
        self.assertTrue(readback["receipt_consistency_verified"])
        self.assertEqual(
            readback["verification"]["verified_files"],
            readback["copy_verification"]["verified_files"],
        )
        self.assertTrue(readback["copy_verification"]["manifest_pin_supplied"])
        self.assertTrue(readback["copy_verification"]["artifact_set_pin_supplied"])
        self.assertFalse(readback["model_loaded"])
        self.assertFalse(readback["clinical_use_allowed"])
        self.assertFalse(readback["training_allowed"])
        self.assertFalse(readback["production_allowed"])


    def test_readback_rejects_package_replacement_during_pinned_read(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )
            package = store / created["store_ref"]
            moved = store / "displaced-package"
            real_reader = __import__(
                "ai.taranom_internal.model_artifacts",
                fromlist=["_read_local_model_manifest_at"],
            )._read_local_model_manifest_at

            def replace_entry_then_read(directory_fd, name):
                if name == "manifest.json":
                    package.rename(moved)
                    package.symlink_to(moved, target_is_directory=True)
                return real_reader(directory_fd, name)

            with patch(
                    "ai.taranom_internal.model_artifacts._read_local_model_manifest_at",
                    side_effect=replace_entry_then_read,
            ):
                with self.assertRaisesRegex(
                        ArtifactRejected, "ARTIFACT_STORE_ENTRY_CHANGED"):
                    verify_local_model_store_entry(store, created["store_ref"])

 
    def test_inventory_rejects_store_root_replacement_during_entry_read(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            store_verified_local_model_artifact(manifest_path, source, store)
            moved_store = root / "displaced-store"
            real_reader = __import__(
                "ai.taranom_internal.model_artifacts",
                fromlist=["_read_local_model_manifest_at"],
            )._read_local_model_manifest_at

            def replace_root_then_read(directory_fd, name):
                if name == "manifest.json":
                    store.rename(moved_store)
                    store.symlink_to(moved_store, target_is_directory=True)
                return real_reader(directory_fd, name)

            with patch(
                    "ai.taranom_internal.model_artifacts._read_local_model_manifest_at",
                    side_effect=replace_root_then_read,
            ):
                with self.assertRaisesRegex(
                        ArtifactRejected, "ARTIFACT_STORE_INVENTORY_CHANGED"):
                    list_local_model_store_entries(store)

 
    def test_inventory_denies_package_over_remaining_byte_budget_before_hashing(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source"
            source.mkdir()
            (source / "weights.part").write_bytes(b"synthetic-model-bytes")
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(manifest()), encoding="utf-8")
            store = root / "store"
            store.mkdir()
            created = store_verified_local_model_artifact(
                manifest_path, source, store
            )

            with patch(
                    "ai.taranom_internal.model_artifacts._MAX_STORE_INVENTORY_BYTES",
                    1,
            ), patch(
                    "ai.taranom_internal.model_artifacts.verify_local_model_manifest",
                    wraps=verify_local_model_manifest,
            ) as verify_payload:
                inventory = list_local_model_store_entries(store)

        self.assertEqual(0, inventory["verified_payload_bytes"])
        self.assertEqual("DENIED", inventory["entries"][0]["status"])
        self.assertEqual(
            "ARTIFACT_STORE_INVENTORY_BYTE_LIMIT_EXCEEDED",
            inventory["entries"][0]["error"],
        )
        verify_payload.assert_not_called()
        self.assertFalse(inventory["model_loaded"])
        self.assertFalse(inventory["training_allowed"])
        self.assertFalse(inventory["clinical_use_allowed"])
        self.assertFalse(inventory["production_allowed"])

    def test_inventory_reserves_budget_before_payload_hash_even_if_receipt_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            store = root / "store"
            store.mkdir()
            refs = []
            payloads = (b"first-synthetic-payload", b"second-larger-synthetic-payload")
            for index, payload in enumerate(payloads):
                source = root / f"source-{index}"
                source.mkdir()
                (source / "weights.part").write_bytes(payload)
                manifest_path = root / f"manifest-{index}.json"
                manifest_path.write_text(
                    json.dumps(manifest(data=payload)), encoding="utf-8"
                )
                created = store_verified_local_model_artifact(
                    manifest_path, source, store
                )
                refs.append(created["store_ref"])

            first_ref, second_ref = sorted(refs)
            receipt_path = store / first_ref / "verification.json"
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            receipt["status"] = "CORRUPTED_AFTER_PAYLOAD_COPY"
            receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
            first_payload_size = len(payloads[refs.index(first_ref)])

            with patch(
                    "ai.taranom_internal.model_artifacts._MAX_STORE_INVENTORY_BYTES",
                    first_payload_size,
            ), patch(
                    "ai.taranom_internal.model_artifacts.verify_local_model_manifest",
                    wraps=verify_local_model_manifest,
            ) as verify_payload:
                inventory = list_local_model_store_entries(store)

        self.assertEqual(0, inventory["verified_payload_bytes"])
        self.assertEqual(first_payload_size, inventory["inventory_payload_bytes_reserved"])
        self.assertEqual([first_ref, second_ref],
                         [entry["store_ref"] for entry in inventory["entries"]])
        self.assertEqual("DENIED", inventory["entries"][0]["status"])
        self.assertEqual("ARTIFACT_STORE_RECEIPT_INVALID",
                         inventory["entries"][0]["error"])
        self.assertEqual("DENIED", inventory["entries"][1]["status"])
        self.assertEqual("ARTIFACT_STORE_INVENTORY_BYTE_LIMIT_EXCEEDED",
                         inventory["entries"][1]["error"])
        verify_payload.assert_called_once()
        self.assertFalse(inventory["model_loaded"])
        self.assertFalse(inventory["training_allowed"])
        self.assertFalse(inventory["clinical_use_allowed"])
        self.assertFalse(inventory["production_allowed"])

if __name__ == "__main__":
    unittest.main()
