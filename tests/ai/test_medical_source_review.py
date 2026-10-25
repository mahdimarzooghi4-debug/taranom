"""TRN-TECH-209: synthetic-only review catalog and recommendation docket tests."""
import copy
import hashlib
import unittest
from unittest.mock import patch

from ai.taranom_internal.medical_knowledge import KnowledgeRejected, freeze_taxonomy
from ai.taranom_internal.medical_source_review import (
    append_review_recommendation, freeze_source_review_catalog,
    open_source_review_docket, source_review_worklist,
)


class SourceReviewDocketTests(unittest.TestCase):
    def setUp(self):
        self.taxonomy = freeze_taxonomy([{
            "category_ref": "category:test-root",
            "display_label": "test:unreviewed category",
            "parent_ref": None, "scheme_ref": "taxscheme:test-v1",
        }])
        self.article = b"fictional source bytes"
        self.rights = b"fictional rights-evidence bytes"
        self.source = {
            "source_ref": "test:source-1",
            "title": "Synthetic source",
            "publisher": "Synthetic publisher",
            "canonical_url": "https://example.org/source/fictional",
            "license_label": "Unverified license",
            "rights_evidence_ref": "synthetic-rights-ref",
            "declared_document_sha256": hashlib.sha256(self.article).hexdigest(),
            "rights_evidence_sha256": hashlib.sha256(self.rights).hexdigest(),
            "taxonomy_refs": ["category:test-root"],
            "intake_ref": "intake:test-1",
        }
        self.package = {
            "source": self.source,
            "article_bytes": self.article,
            "rights_evidence_bytes": self.rights,
        }
        self.catalog = freeze_source_review_catalog([self.package], self.taxonomy)
        self.docket = open_source_review_docket(self.catalog, self.taxonomy)

    def event(self, **kwargs):
        return dict({
            "event_ref": "reviewevent:test-1",
            "source_ref": "test:source-1",
            "package_sha256": self.catalog["sources"][0]["package_sha256"],
            "lane": "RIGHTS",
            "recommendation": "RECOMMEND_ACCEPT",
            "reviewer_ref_claimed": "reviewer:test-legal",
            "evidence_ref_claimed": "evidence:test-file",
            "expected_revision": 0,
        }, **kwargs)

    def append(self, docket=None, request=None, catalog=None, taxonomy=None):
        return append_review_recommendation(
            self.docket if docket is None else docket,
            self.catalog if catalog is None else catalog,
            self.taxonomy if taxonomy is None else taxonomy,
            self.event() if request is None else request,
        )

    def test_catalog_checks_bytes_and_never_returns_them(self):
        self.assertEqual(self.catalog["kind"], "OFFLINE_SOURCE_REVIEW_CATALOG_METADATA_ONLY")
        self.assertEqual(self.catalog["sources"][0]["source_ref"], "test:source-1")
        self.assertFalse(self.catalog["rights_verified"])
        self.assertFalse(self.catalog["training_allowed"])
        self.assertFalse(self.catalog["production_allowed"])
        self.assertNotIn(self.article.decode(), str(self.catalog))
        self.assertNotIn(self.rights.decode(), str(self.catalog))
        with self.assertRaises(KnowledgeRejected):
            freeze_source_review_catalog([
                dict(self.package, article_bytes=b"tampered")
            ], self.taxonomy)
        with self.assertRaises(KnowledgeRejected):
            freeze_source_review_catalog([
                dict(self.package, rights_evidence_bytes=b"tampered")
            ], self.taxonomy)

    def test_catalog_order_and_duplicate_intake_fail_closed(self):
        other = dict(self.source, source_ref="test:source-2", intake_ref="intake:test-2")
        package2 = dict(self.package, source=other)
        a = freeze_source_review_catalog([package2, self.package], self.taxonomy)
        b = freeze_source_review_catalog([self.package, package2], self.taxonomy)
        self.assertEqual(a, b)
        self.assertEqual(len(a["sources"]), 2)
        with self.assertRaises(KnowledgeRejected):
            freeze_source_review_catalog([self.package, self.package], self.taxonomy)
        with self.assertRaises(KnowledgeRejected):
            freeze_source_review_catalog([
                self.package, dict(self.package, source=dict(other, intake_ref="intake:test-1"))
            ], self.taxonomy)

    def test_three_independent_pending_review_lanes(self):
        queue = source_review_worklist(self.docket, self.catalog, self.taxonomy)
        self.assertEqual([x["lane"] for x in queue["items"]],
                         ["RIGHTS", "SCIENCE", "TAXONOMY"])
        self.assertTrue(all(x["status"] == "PENDING" for x in queue["items"]))
        self.assertEqual(len(source_review_worklist(
            self.docket, self.catalog, self.taxonomy, "SCIENCE"
        )["items"]), 1)
        self.assertFalse(queue["reviewer_identity_authenticated"])
        self.assertFalse(queue["evidence_custody_verified"])
        self.assertFalse(queue["training_allowed"])

    def test_human_recommendation_is_append_only_not_approval(self):
        original = copy.deepcopy(self.docket)
        next_docket = self.append()
        self.assertEqual(self.docket, original)
        self.assertEqual(next_docket["revision"], 1)
        self.assertEqual(next_docket["events"][0]["expected_revision"], 0)
        self.assertEqual(next_docket["events"][0]["previous_event_sha256"],
                         self.docket["head_sha256"])
        rights = source_review_worklist(
            next_docket, self.catalog, self.taxonomy, "RIGHTS"
        )
        self.assertEqual(rights["items"][0]["status"], "ACCEPTANCE_RECOMMENDED")
        self.assertFalse(rights["legal_authority_established"])
        self.assertFalse(rights["clinical_use_allowed"])
        self.assertFalse(rights["production_allowed"])
        self.assertEqual(self.append(next_docket), next_docket)

    def test_all_lanes_recommend_accept_still_no_authority(self):
        docket = self.append()
        for revision, lane in enumerate(("SCIENCE", "TAXONOMY"), start=1):
            docket = self.append(docket, self.event(
                event_ref=f"reviewevent:test-{revision+1}",
                lane=lane, expected_revision=revision,
            ))
        queue = source_review_worklist(docket, self.catalog, self.taxonomy)
        self.assertEqual(docket["revision"], 3)
        self.assertTrue(all(x["status"] == "ACCEPTANCE_RECOMMENDED"
                            for x in queue["items"]))
        for key in ("rights_verified", "scientifically_reviewed", "taxonomy_reviewed",
                    "training_allowed", "clinical_use_allowed", "production_allowed"):
            self.assertIs(docket[key], False)
        for key in ("legal_authority_established", "scientific_authority_established",
                    "taxonomy_approved", "training_allowed", "production_allowed"):
            self.assertIs(queue[key], False)

    def test_conflicting_replay_stale_revision_and_bad_scope(self):
        first = self.append()
        with self.assertRaises(KnowledgeRejected):
            self.append(first, self.event(recommendation="RECOMMEND_REJECT"))
        with self.assertRaises(KnowledgeRejected):
            self.append(first, self.event(event_ref="reviewevent:new"))
        with self.assertRaises(KnowledgeRejected):
            self.append(request=self.event(package_sha256="0" * 64))
        with self.assertRaises(KnowledgeRejected):
            self.append(request=self.event(source_ref="test:missing"))
        with self.assertRaises(KnowledgeRejected):
            self.append(request=self.event(lane="PRODUCTION"))

    def test_evidence_request_and_recommendation_revision(self):
        first = self.append(request=self.event(recommendation="REQUEST_EVIDENCE"))
        self.assertEqual(first["work_items"][0]["status"], "EVIDENCE_REQUESTED")
        second = self.append(first, self.event(
            event_ref="reviewevent:revision-2", recommendation="RECOMMEND_REJECT",
            expected_revision=1,
        ))
        self.assertEqual(second["work_items"][0]["status"], "REJECTION_RECOMMENDED")
        self.assertEqual(len(second["events"]), 2)

    def test_reject_event_tampering_projection_tampering_and_forged_authority(self):
        good = self.append()
        mutations = [
            ("events", lambda d: d["events"][0].update(recommendation="RECOMMEND_REJECT")),
            ("events", lambda d: d["events"][0].update(event_sha256="0" * 64)),
            ("events", lambda d: d["events"][0].update(reviewer_ref_claimed="reviewer:other")),
            ("work_items", lambda d: d["work_items"][0].update(status="APPROVED")),
            ("revision", lambda d: d.update(revision=2)),
            ("head_sha256", lambda d: d.update(head_sha256="0" * 64)),
            ("training_allowed", lambda d: d.update(training_allowed=True)),
        ]
        for name, mutate in mutations:
            changed = copy.deepcopy(good)
            mutate(changed)
            with self.subTest(name=name), self.assertRaises(KnowledgeRejected):
                source_review_worklist(changed, self.catalog, self.taxonomy)

    def test_reject_catalog_and_taxonomy_mutation(self):
        for mutation in (
            lambda c: c.update(catalog_sha256="0" * 64),
            lambda c: c["sources"][0].update(package_sha256="0" * 64),
            lambda c: c["sources"][0].update(title="Forged title"),
            lambda c: c.update(rights_verified=True),
        ):
            changed = copy.deepcopy(self.catalog)
            mutation(changed)
            with self.assertRaises(KnowledgeRejected):
                open_source_review_docket(changed, self.taxonomy)
        altered_taxonomy = copy.deepcopy(self.taxonomy)
        altered_taxonomy["taxonomy_sha256"] = "0" * 64
        with self.assertRaises(KnowledgeRejected):
            open_source_review_docket(self.catalog, altered_taxonomy)

    def test_invalid_request_schema_and_identity_claims_fail_closed(self):
        bad_requests = [
            dict(self.event(), extra="untrusted"),
            self.event(reviewer_ref_claimed="not-a-reviewer"),
            self.event(evidence_ref_claimed=""),
            self.event(recommendation="APPROVED"),
            self.event(expected_revision=True),
        ]
        for request in bad_requests:
            with self.subTest(request=request), self.assertRaises(KnowledgeRejected):
                self.append(request=request)
        with self.assertRaises(KnowledgeRejected):
            source_review_worklist(self.docket, self.catalog, self.taxonomy, "BAD")

    def test_event_count_bound_and_replay(self):
        first = self.append()
        with patch("ai.taranom_internal.medical_source_review.MAX_REVIEW_EVENTS", 1):
            self.assertEqual(self.append(first), first)
            with self.assertRaises(KnowledgeRejected):
                self.append(first, self.event(
                    event_ref="reviewevent:new", expected_revision=1
                ))


if __name__ == "__main__":
    unittest.main()
