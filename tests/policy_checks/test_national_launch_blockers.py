"""Fail-close regression for the national launch blocker inventory."""
from __future__ import annotations

import copy
import json
from pathlib import Path
import tempfile
import unittest

from tools.policy_checks.check_national_launch_blockers import (
    ALLOWED_BLOCKERS, MANIFEST, MINIMUM_BLOCKER, POLICIES, REQUIRED,
    audit_inventory, load_inventory, main,
)


REPO = Path(__file__).resolve().parents[2]


class NationalLaunchReadinessTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="trn-launch-gate-")
        self.root = Path(self.temp.name)
        original = load_inventory(REPO / MANIFEST)
        self.inventory = copy.deepcopy(original)
        docs = self.root / "docs" / "decisions"
        docs.mkdir(parents=True)
        all_refs = set(POLICIES)
        for item in original["items"]:
            all_refs.update(item["policy_refs"])
        for ref in all_refs:
            (docs / f"{ref}-synthetic-policy-fixture.md").write_text(
                "Only a test file, NOT a real business decision.",
                encoding="utf-8")

    def tearDown(self) -> None:
        self.temp.cleanup()

    def audit(self, inventory=None) -> list[str]:
        return audit_inventory(self.root, self.inventory if inventory is None else inventory)

    def item(self, name: str):
        return next(x for x in self.inventory["items"] if x["id"] == name)

    def test_real_repository_decision_refs_and_blockers_are_valid(self) -> None:
        self.assertEqual([], audit_inventory(REPO, load_inventory(REPO / MANIFEST)))

    def test_valid_full_manifest_is_still_denied_release(self) -> None:
        self.assertEqual([], self.audit())
        self.assertEqual("DENIED_BY_DEFAULT", self.inventory["release_admission"])
        self.assertIs(self.inventory["real_release_approval_observed"], False)
        self.assertIs(self.inventory["external_evidence_verified"], False)

    def test_exactly_thirteen_epics_three_markets_and_one_national_gate(self) -> None:
        self.assertEqual(17, len(REQUIRED))
        self.assertEqual(set(REQUIRED), {x["id"] for x in self.inventory["items"]})
        self.assertEqual(set(REQUIRED), set(MINIMUM_BLOCKER))
        self.assertIn("DATA_RIGHTS_AND_CONSENT", ALLOWED_BLOCKERS)

    def test_cannot_silently_drop_an_epic(self) -> None:
        self.inventory["items"] = [x for x in self.inventory["items"]
                                   if x["id"] != "TRN-EP-11"]
        self.assertTrue(self.audit())

    def test_cannot_hide_a_market(self) -> None:
        self.inventory["items"] = [x for x in self.inventory["items"]
                                   if x["id"] != "MARKET-CORPORATE"]
        self.assertTrue(self.audit())

    def test_cannot_hide_national_coverage(self) -> None:
        self.inventory["items"] = [x for x in self.inventory["items"]
                                   if x["id"] != "NATIONAL-COVERAGE"]
        self.assertTrue(self.audit())

    def test_duplicate_domain_is_denied(self) -> None:
        self.inventory["items"].append(copy.deepcopy(self.inventory["items"][0]))
        self.assertTrue(any("duplicate" in s for s in self.audit()))

    def test_fake_ready_for_release_is_denied(self) -> None:
        self.inventory["release_admission"] = "READY"
        self.assertTrue(self.audit())

    def test_self_certified_human_release_approval_is_denied(self) -> None:
        self.inventory["real_release_approval_observed"] = True
        self.assertTrue(self.audit())

    def test_self_certified_external_partner_evidence_is_denied(self) -> None:
        self.inventory["external_evidence_verified"] = True
        self.assertTrue(self.audit())

    def test_cannot_change_national_policy_to_limited_pilot(self) -> None:
        self.inventory["scope"] = "REGIONAL_MVP"
        self.assertTrue(self.audit())

    def test_cannot_redefine_three_market_business_basis(self) -> None:
        self.inventory["source_policy_refs"] = ["TRN-BIZ-030"]
        self.assertTrue(self.audit())

    def test_dropping_the_mandatory_per_domain_evidence_fails(self) -> None:
        item = self.item("TRN-EP-11")
        item["missing_evidence"].remove(
            "CONSENTED_AI_DATASET_AND_INDEPENDENT_EVALUATION")
        self.assertTrue(self.audit())

    def test_replacing_actual_provider_contract_evidence_with_unknown_label_fails(self) -> None:
        self.item("TRN-EP-04")["missing_evidence"] = ["PROVIDER_AUTO_APPROVED"]
        self.assertTrue(self.audit())

    def test_empty_evidence_list_is_not_a_release_pass(self) -> None:
        self.item("TRN-EP-08")["missing_evidence"] = []
        self.assertTrue(self.audit())

    def test_duplicate_blocker_cannot_imply_extra_evidence(self) -> None:
        item = self.item("TRN-EP-01")
        item["missing_evidence"].append(item["missing_evidence"][0])
        self.assertTrue(self.audit())

    def test_real_world_identity_or_patient_id_is_not_inventory_content(self) -> None:
        self.item("TRN-EP-01")["id"] = "real:patient-1"
        self.assertTrue(self.audit())

    def test_untrusted_policy_reference_is_not_accepted(self) -> None:
        self.item("TRN-EP-01")["policy_refs"] = ["../../../not-a-policy"]
        self.assertTrue(self.audit())

    def test_missing_decision_source_file_blocks_gate(self) -> None:
        path = self.root / "docs" / "decisions" / "TRN-BIZ-040-synthetic-policy-fixture.md"
        path.unlink()
        self.assertTrue(self.audit())

    def test_symlinked_decision_source_is_blocked(self) -> None:
        path = self.root / "docs" / "decisions" / "TRN-BIZ-040-synthetic-policy-fixture.md"
        path.unlink()
        outside = self.root / "outside.md"
        outside.write_text("not authoritative", encoding="utf-8")
        path.symlink_to(outside)
        self.assertTrue(self.audit())

    def test_extra_unapproved_schema_field_is_denied(self) -> None:
        self.inventory["deploymentApproved"] = True
        self.assertTrue(self.audit())

    def test_unknown_item_schema_field_is_denied(self) -> None:
        self.item("TRN-EP-12")["override"] = "DEPLOY"
        self.assertTrue(self.audit())

    def test_number_one_is_not_true_approval_or_boolean_schema(self) -> None:
        self.inventory["external_evidence_verified"] = 0
        self.assertTrue(self.audit())
        self.inventory["external_evidence_verified"] = False
        self.inventory["schema_version"] = True
        self.assertTrue(self.audit())

    def test_json_duplicate_properties_fail_before_structural_audit(self) -> None:
        file = self.root / "dup.json"
        file.write_text('{"scope":"NATIONAL","scope":"REGIONAL"}', encoding="utf-8")
        with self.assertRaises(ValueError):
            load_inventory(file)

    def test_invalid_json_or_nan_is_rejected(self) -> None:
        file = self.root / "bad.json"
        for contents in ['{"items":', '{"schema_version":NaN}']:
            file.write_text(contents, encoding="utf-8")
            with self.assertRaises(ValueError):
                load_inventory(file)

    def test_cli_success_only_validates_the_denial_inventory(self) -> None:
        target = self.root / MANIFEST
        target.parent.mkdir(parents=True)
        target.write_text(json.dumps(self.inventory), encoding="utf-8")
        report = self.root / "TestResults" / "national-release.json"
        self.assertEqual(0, main(["--root", str(self.root),
                                  "--report-json", str(report)]))
        content = json.loads(report.read_text(encoding="utf-8"))
        self.assertEqual("DENIED", content["release_admission"])
        self.assertIs(content["release_allowed"], False)
        self.assertIs(content["evidence_verified"], False)
        self.assertEqual(set(REQUIRED),
                         {x["id"] for x in content["unverified_domains"]})
        self.inventory["release_admission"] = "READY"
        target.write_text(json.dumps(self.inventory), encoding="utf-8")
        self.assertEqual(1, main(["--root", str(self.root)]))

    def test_missing_manifest_fails_closed(self) -> None:
        self.assertEqual(1, main(["--root", str(self.root)]))

    def test_all_evidence_domains_are_explicit_no_invented_scores(self) -> None:
        for item in self.inventory["items"]:
            for blocker in item["missing_evidence"]:
                self.assertTrue(blocker.isupper())
                self.assertNotIn("THRESHOLD", blocker)
                self.assertNotIn("SCORE", blocker)
        self.assertEqual([], self.audit())


if __name__ == "__main__":
    unittest.main()
