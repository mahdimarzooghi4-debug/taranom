"""TRN-TECH-209: offline metadata catalog and human review recommendation docket.

This is NOT an authenticated approval service or trusted evidence custody.
All identities and evidence references are caller claims. No article/evidence
bytes are persisted, no external fetch, and no medical/training authority.
"""
from __future__ import annotations

import copy
import re

from .medical_knowledge import (
    KnowledgeRejected, _digest, _fields, _label, _ref, _sha, _url,
    _validated_taxonomy, verify_source_package, CATEGORY_REF, INTAKE_REF,
    MAX_TAXONOMY_ASSIGNMENTS, SOURCE_REF,
)

MAX_REVIEW_CATALOG = 32
MAX_REVIEW_EVENTS = 1024
LANES = ("RIGHTS", "SCIENCE", "TAXONOMY")
RECOMMENDATIONS = {
    "REQUEST_EVIDENCE": "EVIDENCE_REQUESTED",
    "RECOMMEND_ACCEPT": "ACCEPTANCE_RECOMMENDED",
    "RECOMMEND_REJECT": "REJECTION_RECOMMENDED",
}
EVENT_REF = re.compile(r"reviewevent:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
REVIEWER_REF = re.compile(r"reviewer:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
EVIDENCE_REF = re.compile(r"evidence:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
PACKAGE_INPUT_FIELDS = frozenset(("source", "article_bytes", "rights_evidence_bytes"))
ENTRY_FIELDS = frozenset((
    "source_ref", "intake_ref", "package_sha256", "title", "publisher",
    "canonical_url", "license_label_unverified", "rights_evidence_ref",
    "article_sha256", "rights_evidence_sha256", "taxonomy_sha256",
    "taxonomy_refs",
))
CATALOG_FIELDS = frozenset((
    "schema_version", "kind", "taxonomy_sha256", "sources", "catalog_sha256",
    "rights_verified", "scientifically_reviewed", "training_allowed",
    "clinical_use_allowed", "production_allowed",
))
DOCKET_FIELDS = frozenset((
    "schema_version", "kind", "catalog_sha256", "taxonomy_sha256",
    "revision", "events", "work_items", "head_sha256",
    "rights_verified", "scientifically_reviewed", "taxonomy_reviewed",
    "training_allowed", "clinical_use_allowed", "production_allowed",
))
EVENT_INPUT_FIELDS = frozenset((
    "event_ref", "source_ref", "package_sha256", "lane", "recommendation",
    "reviewer_ref_claimed", "evidence_ref_claimed", "expected_revision",
))
EVENT_FIELDS = EVENT_INPUT_FIELDS | frozenset(("previous_event_sha256", "event_sha256"))
ITEM_FIELDS = frozenset((
    "source_ref", "package_sha256", "lane", "status", "latest_event_ref",
))


def _no_authority(value, fields):
    if any(value[key] is not False for key in fields):
        raise KnowledgeRejected("FORGED_REVIEW_OR_PRODUCTION_AUTHORITY")


def freeze_source_review_catalog(packages, taxonomy_manifest):
    """Hash-check actual caller-supplied bytes, then retain ONLY metadata.

    A successful call verifies local byte integrity, not publisher ownership,
    license semantics, scientific quality, taxonomy approval or source safety.
    """
    _, taxonomy = _validated_taxonomy(taxonomy_manifest)
    if type(packages) is not list or not 1 <= len(packages) <= MAX_REVIEW_CATALOG:
        raise KnowledgeRejected("BOUNDED_REVIEW_CATALOG_REQUIRED")
    entries = []
    seen_sources = set()
    seen_intakes = set()
    for raw in packages:
        item = _fields(raw, PACKAGE_INPUT_FIELDS)
        checked = verify_source_package(
            item["source"], item["article_bytes"],
            item["rights_evidence_bytes"], taxonomy,
        )
        if checked["source_ref"] in seen_sources or checked["intake_ref"] in seen_intakes:
            raise KnowledgeRejected("DUPLICATE_SOURCE_OR_INTAKE")
        seen_sources.add(checked["source_ref"])
        seen_intakes.add(checked["intake_ref"])
        entries.append({key: checked[key] for key in ENTRY_FIELDS})
    body = {
        "schema_version": 1,
        "kind": "OFFLINE_SOURCE_REVIEW_CATALOG_METADATA_ONLY",
        "taxonomy_sha256": taxonomy["taxonomy_sha256"],
        "sources": sorted(entries, key=lambda x: x["source_ref"]),
    }
    return {
        **body, "catalog_sha256": _digest(body),
        "rights_verified": False, "scientifically_reviewed": False,
        "training_allowed": False, "clinical_use_allowed": False,
        "production_allowed": False,
    }


def _validated_catalog(catalog, taxonomy_manifest):
    category_by_ref, taxonomy = _validated_taxonomy(taxonomy_manifest)
    _fields(catalog, CATALOG_FIELDS)
    _no_authority(catalog, (
        "rights_verified", "scientifically_reviewed", "training_allowed",
        "clinical_use_allowed", "production_allowed",
    ))
    if (type(catalog["schema_version"]) is not int or catalog["schema_version"] != 1
            or catalog["kind"] != "OFFLINE_SOURCE_REVIEW_CATALOG_METADATA_ONLY"
            or catalog["taxonomy_sha256"] != taxonomy["taxonomy_sha256"]
            or type(catalog["sources"]) is not list
            or not 1 <= len(catalog["sources"]) <= MAX_REVIEW_CATALOG):
        raise KnowledgeRejected("REVIEW_CATALOG_SCHEMA_OR_TAXONOMY_CONFLICT")
    seen_sources = set()
    seen_intakes = set()
    normalized = []
    for entry in catalog["sources"]:
        _fields(entry, ENTRY_FIELDS)
        source_ref = _ref(entry["source_ref"], SOURCE_REF)
        intake_ref = _ref(entry["intake_ref"], INTAKE_REF)
        if source_ref in seen_sources or intake_ref in seen_intakes:
            raise KnowledgeRejected("DUPLICATE_SOURCE_OR_INTAKE")
        seen_sources.add(source_ref)
        seen_intakes.add(intake_ref)
        refs = entry["taxonomy_refs"]
        if (type(refs) is not list or not 1 <= len(refs) <= MAX_TAXONOMY_ASSIGNMENTS
                or any(_ref(x, CATEGORY_REF) not in category_by_ref for x in refs)
                or refs != sorted(set(refs))):
            raise KnowledgeRejected("INVALID_CATALOG_TAXONOMY_ASSIGNMENT")
        article_sha = _sha(entry["article_sha256"])
        rights_sha = _sha(entry["rights_evidence_sha256"])
        package_body = {
            "source_ref": source_ref,
            "title": _label(entry["title"]),
            "publisher": _label(entry["publisher"]),
            "canonical_url": _url(entry["canonical_url"]),
            "license_label_unverified": _label(entry["license_label_unverified"]),
            "rights_evidence_ref": _label(entry["rights_evidence_ref"]),
            "article_sha256": article_sha,
            "rights_evidence_sha256": rights_sha,
            "taxonomy_sha256": taxonomy["taxonomy_sha256"],
            "taxonomy_refs": refs,
            "intake_ref": intake_ref,
        }
        if (entry["taxonomy_sha256"] != taxonomy["taxonomy_sha256"]
                or entry["article_sha256"] != article_sha
                or entry["rights_evidence_sha256"] != rights_sha
                or entry["package_sha256"] != _digest(package_body)):
            raise KnowledgeRejected("PACKAGE_FINGERPRINT_CONFLICT")
        normalized.append(dict(entry))
    if normalized != sorted(normalized, key=lambda x: x["source_ref"]):
        raise KnowledgeRejected("CATALOG_ORDER_CONFLICT")
    body = {key: catalog[key] for key in (
        "schema_version", "kind", "taxonomy_sha256", "sources",
    )}
    if _sha(catalog["catalog_sha256"]) != _digest(body):
        raise KnowledgeRejected("CATALOG_FINGERPRINT_CONFLICT")
    return catalog, taxonomy


def _initial_docket(catalog):
    items = [
        {
            "source_ref": entry["source_ref"],
            "package_sha256": entry["package_sha256"],
            "lane": lane, "status": "PENDING", "latest_event_ref": None,
        }
        for entry in catalog["sources"] for lane in LANES
    ]
    return {
        "schema_version": 1,
        "kind": "OFFLINE_REVIEW_RECOMMENDATIONS_ONLY",
        "catalog_sha256": catalog["catalog_sha256"],
        "taxonomy_sha256": catalog["taxonomy_sha256"],
        "revision": 0,
        "events": [],
        "work_items": items,
        "head_sha256": _digest({
            "kind": "REVIEW_DOCKET_GENESIS",
            "catalog_sha256": catalog["catalog_sha256"],
            "taxonomy_sha256": catalog["taxonomy_sha256"],
        }),
        "rights_verified": False, "scientifically_reviewed": False,
        "taxonomy_reviewed": False, "training_allowed": False,
        "clinical_use_allowed": False, "production_allowed": False,
    }


def open_source_review_docket(catalog, taxonomy_manifest):
    """Create three independent recommendation lanes per source, all pending."""
    verified, _ = _validated_catalog(catalog, taxonomy_manifest)
    return _initial_docket(verified)


def _normalize_event(raw):
    _fields(raw, EVENT_INPUT_FIELDS)
    if type(raw["expected_revision"]) is not int or not 0 <= raw["expected_revision"] < MAX_REVIEW_EVENTS:
        raise KnowledgeRejected("INVALID_EXPECTED_REVISION")
    lane = raw["lane"]
    recommendation = raw["recommendation"]
    if type(lane) is not str or lane not in LANES:
        raise KnowledgeRejected("INVALID_REVIEW_LANE")
    if type(recommendation) is not str or recommendation not in RECOMMENDATIONS:
        raise KnowledgeRejected("INVALID_RECOMMENDATION")
    return {
        "event_ref": _ref(raw["event_ref"], EVENT_REF),
        "source_ref": _ref(raw["source_ref"], SOURCE_REF),
        "package_sha256": _sha(raw["package_sha256"]),
        "lane": lane,
        "recommendation": recommendation,
        "reviewer_ref_claimed": _ref(raw["reviewer_ref_claimed"], REVIEWER_REF),
        "evidence_ref_claimed": _ref(raw["evidence_ref_claimed"], EVIDENCE_REF),
        "expected_revision": raw["expected_revision"],
    }


def _apply(docket, event):
    if event["expected_revision"] != docket["revision"]:
        raise KnowledgeRejected("STALE_REVIEW_REVISION")
    item = next((
        x for x in docket["work_items"]
        if x["source_ref"] == event["source_ref"] and x["lane"] == event["lane"]
    ), None)
    if item is None or item["package_sha256"] != event["package_sha256"]:
        raise KnowledgeRejected("REVIEW_PACKAGE_OR_LANE_NOT_FOUND")
    payload = {**event, "previous_event_sha256": docket["head_sha256"]}
    record = {**payload, "event_sha256": _digest(payload)}
    docket["events"].append(record)
    item["status"] = RECOMMENDATIONS[event["recommendation"]]
    item["latest_event_ref"] = event["event_ref"]
    docket["revision"] += 1
    docket["head_sha256"] = record["event_sha256"]
    return record


def _validated_docket(docket, catalog, taxonomy_manifest):
    valid_catalog, _ = _validated_catalog(catalog, taxonomy_manifest)
    _fields(docket, DOCKET_FIELDS)
    _no_authority(docket, (
        "rights_verified", "scientifically_reviewed", "taxonomy_reviewed",
        "training_allowed", "clinical_use_allowed", "production_allowed",
    ))
    events = docket["events"]
    if type(events) is not list or len(events) > MAX_REVIEW_EVENTS:
        raise KnowledgeRejected("BOUNDED_REVIEW_EVENTS_REQUIRED")
    expected = _initial_docket(valid_catalog)
    event_ids = set()
    for raw in events:
        _fields(raw, EVENT_FIELDS)
        event = _normalize_event({key: raw[key] for key in EVENT_INPUT_FIELDS})
        if event["event_ref"] in event_ids:
            raise KnowledgeRejected("DUPLICATE_REVIEW_EVENT")
        event_ids.add(event["event_ref"])
        record = _apply(expected, event)
        if record != raw:
            raise KnowledgeRejected("REVIEW_EVENT_CHAIN_TAMPERED")
    if expected != docket:
        raise KnowledgeRejected("REVIEW_DOCKET_PROJECTION_TAMPERED")
    return docket


def append_review_recommendation(docket, catalog, taxonomy_manifest, request):
    """Append an untrusted, human-supplied recommendation; NEVER approve a source.

    Caller identity, evidence custody and professional qualification are NOT
    authenticated by this offline module. Revision guards reject stale writes.
    Exact event replay is idempotent; conflicting reuse of an ID is rejected.
    """
    _validated_docket(docket, catalog, taxonomy_manifest)
    event = _normalize_event(request)
    for old in docket["events"]:
        if old["event_ref"] == event["event_ref"]:
            if all(old[key] == event[key] for key in EVENT_INPUT_FIELDS):
                return copy.deepcopy(docket)
            raise KnowledgeRejected("REVIEW_EVENT_IDEMPOTENCY_CONFLICT")
    if len(docket["events"]) >= MAX_REVIEW_EVENTS:
        raise KnowledgeRejected("REVIEW_EVENT_LIMIT_REACHED")
    updated = copy.deepcopy(docket)
    _apply(updated, event)
    _validated_docket(updated, catalog, taxonomy_manifest)
    return updated


def source_review_worklist(docket, catalog, taxonomy_manifest, lane=None):
    """Read-only metadata-only queue; recommendations are NOT approvals."""
    _validated_docket(docket, catalog, taxonomy_manifest)
    if lane is not None and (type(lane) is not str or lane not in LANES):
        raise KnowledgeRejected("INVALID_REVIEW_LANE")
    return {
        "status": "OFFLINE_HUMAN_RECOMMENDATIONS_NOT_APPROVALS",
        "catalog_sha256": catalog["catalog_sha256"],
        "revision": docket["revision"],
        "items": copy.deepcopy([
            x for x in docket["work_items"] if lane is None or x["lane"] == lane
        ]),
        "reviewer_identity_authenticated": False,
        "evidence_custody_verified": False,
        "legal_authority_established": False,
        "scientific_authority_established": False,
        "taxonomy_approved": False,
        "training_allowed": False,
        "clinical_use_allowed": False,
        "production_allowed": False,
    }
