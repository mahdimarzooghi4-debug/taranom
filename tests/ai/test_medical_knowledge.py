"""Regression for offline bibliography integrity and synthetic patient-choice route."""
import copy
import os
import unittest
from unittest.mock import patch

from ai.taranom_internal.medical_knowledge import (
    KnowledgeRejected, build_synthetic_route_index,
    freeze_bibliography, suggest_test_options,
)

SOURCE = {
    "source_ref": "test:source-a",
    "title": "Fictional literature metadata",
    "publisher": "Test publisher",
    "canonical_url": "https://example.org/articles/fictional",
    "license_label": "UNREVIEWED",
    "rights_evidence_ref": "pending-review",
    "declared_document_sha256": "a" * 64,
}
LINK = {
    "term": "نشانه ساختگی",
    "specialty_ref": "test:specialty-x",
    "source_ref": "test:source-a",
    "clinical_review_ref": "test:review-unverified",
}
PROVIDER = {
    "provider_ref": "test:doctor-one",
    "specialty_ref": "test:specialty-x",
    "display_label": "test:پزشک فرضی",
    "registry_ref": "test:registry-unverified",
}


class BibliographyTests(unittest.TestCase):
    def test_version_digest_order_invariant_and_unverified(self):
        b = dict(SOURCE, source_ref="pub:another", canonical_url="https://doi.org/10.1/demo")
        first = freeze_bibliography([SOURCE, b])
        other = freeze_bibliography([b, SOURCE])
        self.assertEqual(first, other)
        self.assertFalse(first["clinical_use_allowed"])
        self.assertFalse(first["rights_verified"])
        self.assertFalse(first["training_allowed"])
        self.assertEqual(first["sources"][0]["source_ref"], "pub:another")

    def test_changed_claim_changes_digest_not_authentication(self):
        first = freeze_bibliography([SOURCE])
        changed = freeze_bibliography([dict(SOURCE, license_label="CC-BY")])
        self.assertNotEqual(first["manifest_sha256"], changed["manifest_sha256"])
        self.assertFalse(changed["rights_verified"])

    def test_fail_closed_sources_and_url(self):
        for bad in (
            [SOURCE, SOURCE],
            [dict(SOURCE, canonical_url="http://example.org/a")],
            [dict(SOURCE, canonical_url="https://localhost/a")],
            [dict(SOURCE, canonical_url="https://127.0.0.1/a")],
            [dict(SOURCE, canonical_url="https://example.org@localhost/a")],
            [dict(SOURCE, canonical_url="https://example.org/a#fragment")],
            [dict(SOURCE, declared_document_sha256="wrong")],
            [dict(SOURCE, bogus="extra")],
            [dict(SOURCE, title="bad\nnewline")],
            [],
        ):
            with self.subTest(bad=bad), self.assertRaises(KnowledgeRejected):
                freeze_bibliography(bad)

    def test_non_test_public_bibliography_is_metadata_only(self):
        result = freeze_bibliography([dict(
            SOURCE, source_ref="pub:article-1", license_label="unknown")])
        self.assertEqual(result["kind"], "BIBLIOGRAPHY_METADATA_ONLY")
        self.assertFalse(result["production_allowed"])


class SyntheticMatchingTests(unittest.TestCase):
    def setUp(self):
        self.env = patch.dict(os.environ, {"TARANOM_KNOWLEDGE_QA_ONLY": "CI_EPHEMERAL_ONLY"})
        self.env.start()
        self.addCleanup(self.env.stop)
        self.manifest = freeze_bibliography([SOURCE])

    def build(self, links=None, providers=None, manifest=None):
        return build_synthetic_route_index(
            self.manifest if manifest is None else manifest,
            [LINK] if links is None else links,
            [PROVIDER] if providers is None else providers,
        )

    def test_fictional_persian_route_and_patient_choice(self):
        result = suggest_test_options(self.build(), "این نشانه‌ ساختگی است")
        self.assertEqual(result["status"], "SYNTHETIC_MATCH_ONLY")
        self.assertEqual(result["specialty_options"][0]["specialty_ref"],
                         "test:specialty-x")
        self.assertEqual(result["specialty_options"][0]["source_refs"], ["test:source-a"])
        self.assertFalse(result["specialty_options"][0]["unverified_provider_options"][0]["provider_verified"])
        for flag in ("diagnosis_provided", "doctor_selected", "clinical_use_allowed",
                     "rights_verified", "provider_verified", "production_allowed",
                     "inference_supported"):
            self.assertFalse(result[flag], flag)
        self.assertTrue(result["patient_choice_required"])
        self.assertNotIn("query", result)

    def test_unknown_term_does_not_invent_doctor_or_diagnosis(self):
        result = suggest_test_options(self.build(), "یک عبارت کاملا متفاوت")
        self.assertEqual(result["status"], "NO_SYNTHETIC_MATCH")
        self.assertEqual(result["specialty_options"], [])
        self.assertFalse(result["production_allowed"])

    def test_word_boundaries_prevent_substring_matches(self):
        idx = self.build(links=[dict(LINK, term="درد")])
        self.assertEqual(suggest_test_options(idx, "بی‌درد")["specialty_options"], [])
        self.assertEqual(len(suggest_test_options(idx, "درد")["specialty_options"]), 1)

    def test_multi_option_is_unranked_not_the_best(self):
        link2 = dict(LINK, specialty_ref="test:specialty-b", clinical_review_ref="test:review-b")
        p2 = dict(PROVIDER, provider_ref="test:doctor-two",
                  specialty_ref="test:specialty-b", display_label="test:فرضی دو")
        options = suggest_test_options(
            self.build(links=[LINK, link2], providers=[PROVIDER, p2]),
            "نشانه ساختگی"
        )["specialty_options"]
        self.assertEqual([o["specialty_ref"] for o in options],
                         ["test:specialty-b", "test:specialty-x"])
        self.assertNotIn("score", str(options).lower())
        self.assertNotIn("best", str(options).lower())

    def test_no_provider_means_only_test_specialty(self):
        result = suggest_test_options(self.build(providers=[]), "نشانه ساختگی")
        self.assertEqual(result["specialty_options"][0]["unverified_provider_options"], [])

    def test_reject_missing_lineage_duplicate_and_real_provider(self):
        mutations = [
            ( [dict(LINK, source_ref="test:unknown")], [PROVIDER]),
            ( [LINK, LINK], [PROVIDER]),
            ( [LINK], [PROVIDER, PROVIDER]),
            ( [LINK], [dict(PROVIDER, provider_ref="doctor-real")]),
            ( [LINK], [dict(PROVIDER, display_label="Doctor Name")]),
            ( [dict(LINK, clinical_review_ref="review-approved")], [PROVIDER]),
            ( [LINK], [dict(PROVIDER, specialty_ref="test:unrelated")]),
        ]
        for rules, providers in mutations:
            with self.subTest(rules=rules, providers=providers):
                with self.assertRaises(KnowledgeRejected):
                    self.build(links=rules, providers=providers)

    def test_reject_tampered_manifest_and_forged_authority(self):
        for key, replacement in [
            ("manifest_sha256", "0" * 64),
            ("production_allowed", True),
            ("rights_verified", True),
        ]:
            tampered = copy.deepcopy(self.manifest)
            tampered[key] = replacement
            with self.subTest(key=key), self.assertRaises(KnowledgeRejected):
                self.build(manifest=tampered)

    def test_reject_public_unverified_source_in_synthetic_router(self):
        real_metadata = freeze_bibliography([dict(SOURCE, source_ref="pub:source")])
        with self.assertRaises(KnowledgeRejected):
            self.build(manifest=real_metadata)

    def test_gate_denies_unconfigured_exposure(self):
        idx = self.build()
        with patch.dict(os.environ, {"TARANOM_KNOWLEDGE_QA_ONLY": ""}):
            with self.assertRaises(KnowledgeRejected):
                suggest_test_options(idx, "نشانه ساختگی")
            with self.assertRaises(KnowledgeRejected):
                self.build()


if __name__ == "__main__":
    unittest.main()
