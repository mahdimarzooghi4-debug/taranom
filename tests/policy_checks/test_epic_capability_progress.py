"""TRN-TECH-069: verify candidate capability register is conservative and auditable."""
import copy
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "policy_checks"))
from check_epic_capability_progress import inspect, REGISTRY, BLOCKERS  # noqa: E402


class EpicMeasurementTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        for filename in (REGISTRY, BLOCKERS):
            p = self.root / filename
            p.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / filename, p)
        self.registry = json.loads((self.root / REGISTRY).read_text())
        self.blockers = json.loads((self.root / BLOCKERS).read_text())
        for cap in self.registry["capabilities"]:
            for key in ("implementation_ref", "test_ref"):
                value = cap[key]
                if value is not None:
                    p = self.root / value
                    p.parent.mkdir(parents=True, exist_ok=True)
                    p.write_text("synthetic fixture only; not product acceptance", encoding="utf-8")

    def save(self):
        (self.root / REGISTRY).write_text(json.dumps(self.registry), encoding="utf-8")
        (self.root / BLOCKERS).write_text(json.dumps(self.blockers), encoding="utf-8")

    def denied(self, label):
        self.save()
        v = inspect(self.root)
        self.assertEqual(v["status"], "DENIED_INVALID_MEASUREMENT", v)
        self.assertIn(label, v["errors"])
        self.assertIsNone(v["overall_implementation_completion_pct"])
        self.assertFalse(v["production_ready"])
        self.assertFalse(v["national_release_allowed"])

    def test_current_repository_has_a_valid_but_unapproved_baseline(self):
        report = inspect(ROOT)
        self.assertEqual(report["status"], "DRAFT_EVIDENCE_CATALOG_ONLY", report)
        self.assertEqual(report["epic_count"], 13)
        self.assertEqual(report["provisional_capabilities"], 39)
        self.assertEqual(report["external_blockers_unverified"], 17)
        self.assertIsNone(report["overall_implementation_completion_pct"])
        self.assertIsNone(report["formal_acceptance_pct"])
        self.assertFalse(report["baseline_approved"])
        self.assertFalse(report["national_release_allowed"])
        self.assertEqual(report["formally_accepted_capabilities_recorded"], 0)
        self.assertEqual(len(report["epics"]), 13)
        self.assertTrue(all(x["implementation_completion_pct"] is None for x in report["epics"]))

    def test_ordinary_fake_repo_can_only_catalog_evidence(self):
        self.save()
        r = inspect(self.root)
        self.assertEqual(r["status"], "DRAFT_EVIDENCE_CATALOG_ONLY")
        self.assertEqual(
            r["references_to_test_only_or_disconnected_code"] +
            r["capabilities_without_code_test_links"], 39)

    def test_all_thirteen_epics_and_national_market_boundaries_still_required(self):
        self.blockers["items"] = [x for x in self.blockers["items"] if x["id"] != "MARKET-CORPORATE"]
        self.denied("RELEASE_BLOCKERS_UNVERIFIED_REQUIRED")

    def test_missing_epic_not_silent_denominator_shrink(self):
        self.registry["capabilities"] = self.registry["capabilities"][:-3]
        self.denied("CANDIDATE_CAPABILITY_SCOPE_CHANGED")

    def test_duplicate_capability_cannot_inflate_count(self):
        self.registry["capabilities"][1] = copy.deepcopy(self.registry["capabilities"][0])
        self.denied("CAPABILITY_EPIC_ID_INVALID_OR_DUPLICATE")

    def test_capability_cannot_be_assigned_to_different_epic(self):
        self.registry["capabilities"][0]["epic_id"] = "TRN-EP-02"
        self.denied("CAPABILITY_EPIC_ID_INVALID_OR_DUPLICATE")

    def test_missing_or_stale_code_reference_denied(self):
        cap = self.registry["capabilities"][0]
        (self.root / cap["implementation_ref"]).unlink()
        self.denied("MISSING_OR_UNSAFE_EVIDENCE_REFERENCE")

    def test_missing_test_reference_denied(self):
        cap = self.registry["capabilities"][0]
        (self.root / cap["test_ref"]).unlink()
        self.denied("MISSING_OR_UNSAFE_EVIDENCE_REFERENCE")

    def test_self_accepted_capability_forbidden(self):
        self.registry["capabilities"][0]["formal_acceptance_observed"] = True
        self.denied("UNVERIFIED_CAPABILITY_CANNOT_SELF_APPROVE")

    def test_self_approved_weights_forbidden(self):
        self.registry["weights_status"] = "APPROVED"
        self.denied("MEASUREMENT_BASELINE_INVALID")

    def test_arbitrary_numeric_progress_field_forbidden(self):
        self.registry["overall_progress_pct"] = 99
        self.denied("MEASUREMENT_BASELINE_INVALID")

    def test_no_evidence_cannot_be_given_code_reference(self):
        cap = next(c for c in self.registry["capabilities"] if c["evidence_class"] == "NO_EVIDENCE")
        cap["implementation_ref"] = self.registry["capabilities"][0]["implementation_ref"]
        self.denied("CANNOT_COUNT_UNVERIFIED_CODE_EVIDENCE")

    def test_evidence_class_cannot_be_promoted_to_real_system(self):
        self.registry["capabilities"][0]["evidence_class"] = "PRODUCTION_READY"
        self.denied("CAPABILITY_EVIDENCE_CLASS_UNSUPPORTED")

    def test_missing_associated_business_provenance_denied(self):
        self.registry["capabilities"][0]["decision_refs"] = []
        self.denied("CAPABILITY_PROVENANCE_MISMATCH")

    def test_attempt_to_mark_national_release_ready_denied(self):
        self.blockers["release_admission"] = "READY"
        self.denied("RELEASE_BLOCKERS_UNVERIFIED_REQUIRED")

    def test_member_market_unverified_cannot_be_self_promoted(self):
        target = next(x for x in self.blockers["items"] if x["id"] == "MARKET-PUBLIC")
        target["status"] = "VERIFIED"
        self.denied("RELEASE_BLOCKERS_UNVERIFIED_REQUIRED")

    def test_repository_external_path_escape_is_rejected(self):
        self.registry["capabilities"][0]["implementation_ref"] = "src/../../home/secret.py"
        self.denied("UNSAFE_OR_UNMAPPED_EVIDENCE_REFERENCE")

    def test_symlinked_file_cannot_count_as_present_code(self):
        cap = self.registry["capabilities"][0]
        original = self.root / cap["implementation_ref"]
        original.unlink()
        original.symlink_to(self.root / self.registry["capabilities"][1]["test_ref"] if self.registry["capabilities"][1]["test_ref"] else self.root / self.registry["capabilities"][2]["test_ref"])
        self.denied("MISSING_OR_UNSAFE_EVIDENCE_REFERENCE")

    def test_raw_synthetic_evidence_does_not_automatically_accept_epic(self):
        self.save()
        result = inspect(self.root)
        self.assertTrue(result["references_to_test_only_or_disconnected_code"] > 0)
        self.assertTrue(all(x["formally_accepted_capabilities_recorded"] == 0
                            and x["formal_acceptance_status"] == "NOT_VERIFIED"
                            for x in result["epics"]))
        self.assertIsNone(result["overall_implementation_completion_pct"])

    def test_source_json_duplicate_keys_denied(self):
        (self.root / REGISTRY).write_text('{"schema_version":1,"schema_version":1}', encoding="utf-8")
        report = inspect(self.root)
        self.assertIn("DUPLICATE_JSON_KEY", report["errors"])


if __name__ == "__main__":
    unittest.main()
