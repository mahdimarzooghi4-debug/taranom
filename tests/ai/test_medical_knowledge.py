"""Regression for offline bibliography integrity and synthetic patient-choice route."""
import copy
import hashlib
import os
import unittest
from unittest.mock import patch

from ai.taranom_internal.medical_knowledge import (
    KnowledgeRejected, build_synthetic_route_index,
    freeze_bibliography, freeze_taxonomy, search_bibliography,
    suggest_test_options, verify_source_package,
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

    def test_search_real_metadata_stays_bibliographic_and_unverified(self):
        item = dict(SOURCE, source_ref="pub:metadata-a")
        snapshot = freeze_bibliography([item])
        result = search_bibliography(snapshot, "fictional literature")
        self.assertEqual(result["status"], "BIBLIOGRAPHIC_METADATA_RESULTS_ONLY")
        self.assertEqual(len(result["sources"]), 1)
        self.assertEqual(result["sources"][0]["source_ref"], "pub:metadata-a")
        self.assertFalse(result["rights_verified"])
        self.assertFalse(result["training_allowed"])
        self.assertFalse(result["clinical_use_allowed"])
        self.assertFalse(result["production_allowed"])
        self.assertEqual(search_bibliography(snapshot, "not present")["sources"], [])

    def test_search_denies_source_or_authority_tampering(self):
        snapshot = freeze_bibliography([SOURCE])
        for bad in (
            dict(snapshot, rights_verified=True),
            dict(snapshot, manifest_sha256="0" * 64),
        ):
            with self.assertRaises(KnowledgeRejected):
                search_bibliography(bad, "fictional")
        self.assertRaises(KnowledgeRejected, search_bibliography, snapshot, "")

    def test_non_test_public_bibliography_is_metadata_only(self):
        result = freeze_bibliography([dict(
            SOURCE, source_ref="pub:article-1", license_label="unknown")])
        self.assertEqual(result["kind"], "BIBLIOGRAPHY_METADATA_ONLY")
        self.assertFalse(result["production_allowed"])


class SourceIntakeAndTaxonomyTests(unittest.TestCase):
    def setUp(self):
        self.root = {
            "category_ref": "category:test-root",
            "display_label": "test:root",
            "parent_ref": None,
            "scheme_ref": "taxscheme:test-v1",
        }
        self.child = {
            "category_ref": "category:test-topic",
            "display_label": "test:topic",
            "parent_ref": "category:test-root",
            "scheme_ref": "taxscheme:test-v1",
        }
        self.taxonomy = freeze_taxonomy([self.root, self.child])
        self.article = b"synthetic article fixture bytes"
        self.rights = b"synthetic license evidence fixture"
        self.source = dict(
            SOURCE,
            source_ref="test:source-intake",
            declared_document_sha256=hashlib.sha256(self.article).hexdigest(),
            rights_evidence_sha256=hashlib.sha256(self.rights).hexdigest(),
            taxonomy_refs=["category:test-topic"],
            intake_ref="intake:test-001",
        )

    def verify(self, source=None, article=None, rights=None, taxonomy=None):
        return verify_source_package(
            self.source if source is None else source,
            self.article if article is None else article,
            self.rights if rights is None else rights,
            self.taxonomy if taxonomy is None else taxonomy,
        )

    def test_taxonomy_is_configurable_deterministic_and_unapproved(self):
        self.assertEqual(
            self.taxonomy, freeze_taxonomy([self.child, self.root]))
        self.assertFalse(self.taxonomy["clinical_use_allowed"])
        self.assertFalse(self.taxonomy["training_allowed"])
        self.assertFalse(self.taxonomy["production_allowed"])
        self.assertTrue(all(
            item["scientifically_reviewed"] is False
            for item in self.taxonomy["categories"]
        ))

    def test_taxonomy_rejects_duplicates_missing_parent_and_cycles(self):
        for categories in (
            [self.root, self.root],
            [dict(self.child, parent_ref="category:missing")],
            [
                dict(self.root, parent_ref="category:test-topic"),
                self.child,
            ],
        ):
            with self.subTest(categories=categories), self.assertRaises(KnowledgeRejected):
                freeze_taxonomy(categories)

    def test_taxonomy_tampering_or_forged_authority_is_denied(self):
        for key, value in (
            ("taxonomy_sha256", "0" * 64),
            ("training_allowed", True),
        ):
            changed = copy.deepcopy(self.taxonomy)
            changed[key] = value
            with self.subTest(key=key), self.assertRaises(KnowledgeRejected):
                self.verify(taxonomy=changed)

    def test_source_package_checks_actual_bytes_but_never_approves_rights(self):
        result = self.verify()
        self.assertEqual(result["status"], "SOURCE_PACKAGE_INTEGRITY_VERIFIED_ONLY")
        self.assertEqual(result["article_sha256"], hashlib.sha256(self.article).hexdigest())
        self.assertEqual(result["rights_evidence_sha256"], hashlib.sha256(self.rights).hexdigest())
        self.assertEqual(result["taxonomy_refs"], ["category:test-topic"])
        self.assertTrue(result["source_bytes_integrity_verified"])
        self.assertTrue(result["rights_evidence_bytes_integrity_verified"])
        for key in (
            "rights_verified", "license_semantics_reviewed",
            "scientifically_reviewed", "taxonomy_reviewed",
            "training_allowed", "clinical_use_allowed", "production_allowed",
            "verifier_persisted_source_bytes",
            "verifier_persisted_rights_evidence_bytes",
        ):
            self.assertFalse(result[key], key)
        self.assertNotIn("article_bytes", result)
        self.assertNotIn("rights_evidence_bytes", result)
        self.assertNotIn(self.article.decode(), str(result))
        self.assertNotIn(self.rights.decode(), str(result))

    def test_source_package_rejects_article_and_rights_digest_mismatch(self):
        with self.assertRaises(KnowledgeRejected):
            self.verify(source=dict(self.source, declared_document_sha256="0" * 64))
        with self.assertRaises(KnowledgeRejected):
            self.verify(source=dict(self.source, rights_evidence_sha256="0" * 64))

    def test_source_package_rejects_unknown_or_duplicate_category(self):
        with self.assertRaises(KnowledgeRejected):
            self.verify(source=dict(self.source, taxonomy_refs=["category:unknown"]))
        with self.assertRaises(KnowledgeRejected):
            self.verify(source=dict(
                self.source,
                taxonomy_refs=["category:test-topic", "category:test-topic"],
            ))

    def test_source_package_rejects_bad_schema_and_nonbytes(self):
        with self.assertRaises(KnowledgeRejected):
            self.verify(source=dict(self.source, unexpected="field"))
        with self.assertRaises(KnowledgeRejected):
            self.verify(article=bytearray(self.article))
        with self.assertRaises(KnowledgeRejected):
            self.verify(rights="not bytes")

    def test_source_and_evidence_byte_limits_are_enforced(self):
        with patch("ai.taranom_internal.medical_knowledge.MAX_ARTIFACT_BYTES", 4):
            with self.assertRaises(KnowledgeRejected):
                self.verify()
        with patch("ai.taranom_internal.medical_knowledge.MAX_RIGHTS_EVIDENCE_BYTES", 4):
            with self.assertRaises(KnowledgeRejected):
                self.verify()


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
