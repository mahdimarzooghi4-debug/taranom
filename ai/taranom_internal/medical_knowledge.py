"""TRN-TECH-206: offline provenance-first literature catalog and synthetic routing QA.

Public-source metadata is UNREVIEWED, never a license attestation. Matching is
restricted to explicit test: fixtures; it cannot diagnose or select a real doctor.
No network, model training, live patient data or Production inference.
"""
from __future__ import annotations

import hashlib
import ipaddress
import json
import os
import re
import unicodedata
from urllib.parse import urlsplit


class KnowledgeRejected(ValueError):
    """A missing right, invalid lineage or non-test matching request fails closed."""


SOURCE_FIELDS = frozenset((
    "source_ref", "title", "publisher", "canonical_url",
    "license_label", "rights_evidence_ref", "declared_document_sha256",
))
LINK_FIELDS = frozenset(("term", "specialty_ref", "source_ref", "clinical_review_ref"))
PROVIDER_FIELDS = frozenset(("provider_ref", "specialty_ref", "display_label", "registry_ref"))
TEST_REF = re.compile(r"test:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
SOURCE_REF = re.compile(r"(?:pub|test):[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
SHA256 = re.compile(r"[0-9a-fA-F]{64}\Z", re.ASCII)
LABEL = re.compile(r"[^\x00-\x1f\x7f]{1,240}\Z", re.UNICODE)
DIACRITICS = re.compile("[\u064b-\u065f\u0670\u0640]")
TOKEN = re.compile(r"[^\W_]+", re.UNICODE)
MAX_SOURCES = 2048
MAX_LINKS = 256
MAX_PROVIDERS = 256


def _fields(value, expected):
    if type(value) is not dict or set(value) != expected:
        raise KnowledgeRejected("EXACT_SCHEMA_REQUIRED")
    return value


def _label(value):
    if type(value) is not str or not LABEL.fullmatch(value) or value != value.strip():
        raise KnowledgeRejected("BOUNDED_NONEMPTY_LABEL_REQUIRED")
    return value


def _ref(value, pattern):
    if type(value) is not str or pattern.fullmatch(value) is None:
        raise KnowledgeRejected("INVALID_REFERENCE")
    return value


def _sha(value):
    if type(value) is not str or SHA256.fullmatch(value) is None:
        raise KnowledgeRejected("SHA256_DECLARATION_REQUIRED")
    return value.lower()


def _url(value):
    if type(value) is not str or len(value) > 512 or any(ord(x) < 33 for x in value):
        raise KnowledgeRejected("INVALID_SOURCE_URL")
    try:
        parsed = urlsplit(value)
        host = parsed.hostname
        if (parsed.scheme != "https" or not host or not parsed.path
                or parsed.username is not None or parsed.password is not None
                or parsed.port not in (None, 443) or parsed.fragment):
            raise KnowledgeRejected("HTTPS_SOURCE_URL_REQUIRED")
        if (len(host) > 253 or host.lower() in ("localhost", "localhost.localdomain")
                or host.endswith((".local", ".internal", ".test"))
                or "." not in host or not re.fullmatch(r"[A-Za-z0-9.-]+", host)):
            raise KnowledgeRejected("PUBLIC_SOURCE_HOST_REQUIRED")
        try:
            ipaddress.ip_address(host)
        except ValueError:
            pass
        else:
            raise KnowledgeRejected("IP_LITERAL_SOURCE_FORBIDDEN")
    except (ValueError, TypeError) as exc:
        raise KnowledgeRejected("INVALID_SOURCE_URL") from exc
    return value


def _digest(obj):
    raw = json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(raw.encode("ascii")).hexdigest()


def freeze_bibliography(sources):
    """Freeze source metadata, not article text or permission to reuse it.

    rights_evidence_ref and declared_document_sha256 are *claims* until checked
    against independent documents/bytes by an authorized human workflow.
    """
    if type(sources) is not list or not 1 <= len(sources) <= MAX_SOURCES:
        raise KnowledgeRejected("BOUNDED_SOURCE_SET_REQUIRED")
    entries = []
    seen = set()
    for raw in sources:
        s = _fields(raw, SOURCE_FIELDS)
        ref = _ref(s["source_ref"], SOURCE_REF)
        if ref in seen:
            raise KnowledgeRejected("DUPLICATE_SOURCE")
        seen.add(ref)
        entries.append({
            "source_ref": ref,
            "title": _label(s["title"]),
            "publisher": _label(s["publisher"]),
            "canonical_url": _url(s["canonical_url"]),
            "license_label": _label(s["license_label"]),
            "rights_evidence_ref": _label(s["rights_evidence_ref"]),
            "declared_document_sha256": _sha(s["declared_document_sha256"]),
            "rights_verified": False,
            "scientifically_reviewed": False,
            "training_allowed": False,
        })
    canonical = sorted(entries, key=lambda x: x["source_ref"])
    body = {"schema_version": 1, "kind": "BIBLIOGRAPHY_METADATA_ONLY",
            "sources": canonical}
    return {**body, "manifest_sha256": _digest(body),
            "rights_verified": False, "clinical_use_allowed": False,
            "training_allowed": False, "production_allowed": False}


def _words(value):
    text = unicodedata.normalize("NFKC", _label(value)).casefold()
    text = text.replace("ي", "ی").replace("ك", "ک")
    text = text.replace("\u200c", "").replace("\u200d", "")
    text = DIACRITICS.sub("", text)
    words = TOKEN.findall(text)
    if not words or len(words) > 48:
        raise KnowledgeRejected("INVALID_SEARCH_TERMS")
    return tuple(words)


def _test_gate():
    if os.environ.get("TARANOM_KNOWLEDGE_QA_ONLY") != "CI_EPHEMERAL_ONLY":
        raise KnowledgeRejected("SYNTHETIC_QA_GATE_REQUIRED")


def build_synthetic_route_index(manifest, links, providers):
    """Create traceable, NOT medically validated test-only specialty matches."""
    _test_gate()
    if type(manifest) is not dict or set(manifest) != {
        "schema_version", "kind", "sources", "manifest_sha256",
        "rights_verified", "clinical_use_allowed", "training_allowed", "production_allowed"
    }:
        raise KnowledgeRejected("FROZEN_MANIFEST_REQUIRED")
    body = {key: manifest[key] for key in ("schema_version", "kind", "sources")}
    if (body["schema_version"] != 1 or body["kind"] != "BIBLIOGRAPHY_METADATA_ONLY"
            or manifest["manifest_sha256"] != _digest(body)
            or any(manifest[k] is not False for k in (
                "rights_verified", "clinical_use_allowed", "training_allowed",
                "production_allowed"))):
        raise KnowledgeRejected("MANIFEST_INTEGRITY_OR_AUTHORITY_CONFLICT")
    source_ids = set()
    for s in body["sources"]:
        if type(s) is not dict or _ref(s.get("source_ref"), TEST_REF) in source_ids:
            raise KnowledgeRejected("SYNTHETIC_SOURCE_REQUIRED")
        source_ids.add(s["source_ref"])
        if any(s.get(flag) is not False for flag in (
            "rights_verified", "scientifically_reviewed", "training_allowed")):
            raise KnowledgeRejected("FORGED_REVIEW_AUTHORITY")
    if (not source_ids or type(links) is not list or not 1 <= len(links) <= MAX_LINKS
            or type(providers) is not list or len(providers) > MAX_PROVIDERS):
        raise KnowledgeRejected("BOUNDED_QA_INDEX_REQUIRED")
    rules = []
    rule_ids = set()
    specialties = set()
    for raw in links:
        x = _fields(raw, LINK_FIELDS)
        specialty = _ref(x["specialty_ref"], TEST_REF)
        source = _ref(x["source_ref"], TEST_REF)
        _ref(x["clinical_review_ref"], TEST_REF)  # unverified test claim
        if source not in source_ids:
            raise KnowledgeRejected("UNREFERENCED_SOURCE")
        term = _words(x["term"])
        key = (term, specialty, source)
        if key in rule_ids:
            raise KnowledgeRejected("DUPLICATE_KNOWLEDGE_LINK")
        rule_ids.add(key)
        specialties.add(specialty)
        rules.append((term, specialty, source, x["clinical_review_ref"]))
    doctors = []
    doctor_ids = set()
    for raw in providers:
        x = _fields(raw, PROVIDER_FIELDS)
        ident = _ref(x["provider_ref"], TEST_REF)
        specialty = _ref(x["specialty_ref"], TEST_REF)
        _ref(x["registry_ref"], TEST_REF)  # not a validated provider registry
        if ident in doctor_ids or specialty not in specialties:
            raise KnowledgeRejected("DUPLICATE_OR_UNRELATED_PROVIDER")
        doctor_ids.add(ident)
        if not _label(x["display_label"]).startswith("test:"):
            raise KnowledgeRejected("REAL_DOCTOR_LABEL_FORBIDDEN")
        doctors.append((ident, specialty, x["display_label"], x["registry_ref"]))
    return {
        "kind": "SYNTHETIC_QA_ROUTE_INDEX",
        "source_manifest_sha256": manifest["manifest_sha256"],
        "rules": tuple(sorted(rules)),
        "providers": tuple(sorted(doctors)),
    }


def suggest_test_options(index, query):
    """Find evidence-linked synthetic options, never rank or choose a doctor."""
    _test_gate()
    if type(index) is not dict or set(index) != {
        "kind", "source_manifest_sha256", "rules", "providers"
    } or index["kind"] != "SYNTHETIC_QA_ROUTE_INDEX":
        raise KnowledgeRejected("SYNTHETIC_INDEX_REQUIRED")
    words = _words(query)
    result = {}
    for term, specialty, source, review in index["rules"]:
        if any(words[i:i + len(term)] == term
               for i in range(len(words) - len(term) + 1)):
            record = result.setdefault(specialty, {"sources": set(), "reviews": set()})
            record["sources"].add(source)
            record["reviews"].add(review)
    options = []
    for specialty in sorted(result):
        sources = result[specialty]
        choices = [{
            "provider_ref": doc_id, "display_label": label,
            "unverified_registry_ref": registry,
            "provider_verified": False,
        } for doc_id, sp, label, registry in index["providers"] if sp == specialty]
        options.append({
            "specialty_ref": specialty, "source_refs": sorted(sources["sources"]),
            "unverified_review_refs": sorted(sources["reviews"]),
            "unverified_provider_options": choices,
            "clinical_validity_established": False,
        })
    return {
        "status": "SYNTHETIC_MATCH_ONLY" if options else "NO_SYNTHETIC_MATCH",
        "source_manifest_sha256": index["source_manifest_sha256"],
        "specialty_options": options,
        "diagnosis_provided": False, "doctor_selected": False,
        "patient_choice_required": True, "clinical_use_allowed": False,
        "rights_verified": False, "provider_verified": False,
        "production_allowed": False, "inference_supported": False,
    }


def search_bibliography(manifest, query):
    """Search only titles/publishers of frozen public metadata; NEVER clinical advice.

    No URL retrieval, abstract/body indexing, rights approval, or patient matching.
    The returned citation metadata remains explicitly unverified.
    """
    expected = {"schema_version", "kind", "sources", "manifest_sha256",
                "rights_verified", "clinical_use_allowed",
                "training_allowed", "production_allowed"}
    if type(manifest) is not dict or set(manifest) != expected:
        raise KnowledgeRejected("FROZEN_MANIFEST_REQUIRED")
    body = {key: manifest[key] for key in ("schema_version", "kind", "sources")}
    if (body["schema_version"] != 1
            or body["kind"] != "BIBLIOGRAPHY_METADATA_ONLY"
            or type(body["sources"]) is not list
            or not 1 <= len(body["sources"]) <= MAX_SOURCES
            or manifest["manifest_sha256"] != _digest(body)
            or any(manifest[key] is not False for key in (
                "rights_verified", "clinical_use_allowed",
                "training_allowed", "production_allowed"))):
        raise KnowledgeRejected("UNTRUSTED_BIBLIOGRAPHY")
    q = _words(query)
    matches = []
    seen = set()
    stored_fields = SOURCE_FIELDS | {
        "rights_verified", "scientifically_reviewed", "training_allowed"
    }
    for source in body["sources"]:
        _fields(source, stored_fields)
        ref = _ref(source["source_ref"], SOURCE_REF)
        if ref in seen or any(source[k] is not False for k in (
                "rights_verified", "scientifically_reviewed", "training_allowed")):
            raise KnowledgeRejected("INVALID_SOURCE_LINEAGE_OR_AUTHORITY")
        seen.add(ref)
        for field in ("title", "publisher"):
            words = _words(source[field])
            if any(words[i:i + len(q)] == q
                   for i in range(len(words) - len(q) + 1)):
                matches.append({
                    "source_ref": ref,
                    "title": source["title"],
                    "publisher": source["publisher"],
                    "canonical_url": source["canonical_url"],
                    "license_label_unverified": source["license_label"],
                    "rights_verified": False,
                })
                break
    matches.sort(key=lambda x: x["source_ref"])
    return {
        "status": "BIBLIOGRAPHIC_METADATA_RESULTS_ONLY",
        "manifest_sha256": manifest["manifest_sha256"],
        "sources": matches[:20], "truncated": len(matches) > 20,
        "clinical_use_allowed": False, "training_allowed": False,
        "rights_verified": False, "production_allowed": False,
    }


# TRN-TECH-208: configurable taxonomy and local source-package integrity.
CATEGORY_REF = re.compile(r"category:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
TAXONOMY_SCHEME_REF = re.compile(r"taxscheme:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
INTAKE_REF = re.compile(r"intake:[A-Za-z0-9_:-]{1,120}\Z", re.ASCII)
TAXONOMY_FIELDS = frozenset((
    "category_ref", "display_label", "parent_ref", "scheme_ref",
))
PACKAGE_FIELDS = SOURCE_FIELDS | frozenset((
    "rights_evidence_sha256", "taxonomy_refs", "intake_ref",
))
MAX_TAXONOMY_CATEGORIES = 512
MAX_TAXONOMY_ASSIGNMENTS = 32
MAX_ARTIFACT_BYTES = 25 * 1024 * 1024
MAX_RIGHTS_EVIDENCE_BYTES = 2 * 1024 * 1024


def freeze_taxonomy(categories):
    """Freeze a caller-supplied taxonomy structure without choosing clinical labels.

    Category labels and hierarchy are supplied by the owner/qualified reviewers;
    this function checks structure only. A frozen taxonomy is not clinically
    approved, usable for routing, or authorized for model training.
    """
    if (type(categories) is not list or
            not 1 <= len(categories) <= MAX_TAXONOMY_CATEGORIES):
        raise KnowledgeRejected("BOUNDED_TAXONOMY_REQUIRED")
    entries = []
    seen = set()
    schemes = set()
    for raw in categories:
        item = _fields(raw, TAXONOMY_FIELDS)
        ref = _ref(item["category_ref"], CATEGORY_REF)
        if ref in seen:
            raise KnowledgeRejected("DUPLICATE_TAXONOMY_CATEGORY")
        seen.add(ref)
        parent = item["parent_ref"]
        if parent is not None:
            parent = _ref(parent, CATEGORY_REF)
        scheme = _ref(item["scheme_ref"], TAXONOMY_SCHEME_REF)
        schemes.add(scheme)
        entries.append({
            "category_ref": ref,
            "display_label": _label(item["display_label"]),
            "parent_ref": parent,
            "scheme_ref": scheme,
            "scientifically_reviewed": False,
        })
    if len(schemes) != 1:
        raise KnowledgeRejected("SINGLE_TAXONOMY_SCHEME_REQUIRED")
    by_ref = {entry["category_ref"]: entry for entry in entries}
    for entry in entries:
        parent = entry["parent_ref"]
        if parent is not None and parent not in by_ref:
            raise KnowledgeRejected("TAXONOMY_PARENT_NOT_FOUND")
    for entry in entries:
        seen_path = set()
        current = entry["category_ref"]
        while current is not None:
            if current in seen_path:
                raise KnowledgeRejected("TAXONOMY_CYCLE")
            seen_path.add(current)
            current = by_ref[current]["parent_ref"]
    canonical = sorted(entries, key=lambda item: item["category_ref"])
    body = {
        "schema_version": 1,
        "kind": "CONFIGURABLE_TAXONOMY_METADATA_ONLY",
        "categories": canonical,
    }
    return {
        **body,
        "taxonomy_sha256": _digest(body),
        "clinical_use_allowed": False,
        "training_allowed": False,
        "production_allowed": False,
    }


def _validated_taxonomy(manifest):
    expected = {
        "schema_version", "kind", "categories", "taxonomy_sha256",
        "clinical_use_allowed", "training_allowed", "production_allowed",
    }
    if type(manifest) is not dict or set(manifest) != expected:
        raise KnowledgeRejected("FROZEN_TAXONOMY_REQUIRED")
    if (manifest["schema_version"] != 1
            or manifest["kind"] != "CONFIGURABLE_TAXONOMY_METADATA_ONLY"
            or any(manifest[key] is not False for key in (
                "clinical_use_allowed", "training_allowed", "production_allowed"))):
        raise KnowledgeRejected("TAXONOMY_AUTHORITY_CONFLICT")
    categories = manifest["categories"]
    if type(categories) is not list:
        raise KnowledgeRejected("BOUNDED_TAXONOMY_REQUIRED")
    raw = []
    for item in categories:
        _fields(item, TAXONOMY_FIELDS | {"scientifically_reviewed"})
        if item["scientifically_reviewed"] is not False:
            raise KnowledgeRejected("UNVERIFIED_TAXONOMY_CLAIM")
        raw.append({key: item[key] for key in TAXONOMY_FIELDS})
    rebuilt = freeze_taxonomy(raw)
    if (rebuilt["categories"] != categories
            or rebuilt["taxonomy_sha256"] != manifest["taxonomy_sha256"]):
        raise KnowledgeRejected("TAXONOMY_INTEGRITY_MISMATCH")
    return {item["category_ref"]: item for item in rebuilt["categories"]}, rebuilt


def verify_source_package(source, article_bytes, rights_evidence_bytes, taxonomy_manifest):
    """Verify byte digests and taxonomy references in memory; never persist or fetch bytes.

    A matching digest proves byte integrity only. It does NOT establish that a
    source is public domain, that its license permits reuse/training, or that the
    taxonomy or article is medically correct. Human/legal review stays separate.
    """
    item = _fields(source, PACKAGE_FIELDS)
    if (type(article_bytes) is not bytes or
            not 1 <= len(article_bytes) <= MAX_ARTIFACT_BYTES):
        raise KnowledgeRejected("BOUNDED_ARTICLE_BYTES_REQUIRED")
    if (type(rights_evidence_bytes) is not bytes or
            not 1 <= len(rights_evidence_bytes) <= MAX_RIGHTS_EVIDENCE_BYTES):
        raise KnowledgeRejected("BOUNDED_RIGHTS_EVIDENCE_BYTES_REQUIRED")
    source_ref = _ref(item["source_ref"], SOURCE_REF)
    title = _label(item["title"])
    publisher = _label(item["publisher"])
    canonical_url = _url(item["canonical_url"])
    license_label = _label(item["license_label"])
    rights_ref = _label(item["rights_evidence_ref"])
    intake_ref = _ref(item["intake_ref"], INTAKE_REF)
    declared_article_sha = _sha(item["declared_document_sha256"])
    declared_rights_sha = _sha(item["rights_evidence_sha256"])
    actual_article_sha = hashlib.sha256(article_bytes).hexdigest()
    actual_rights_sha = hashlib.sha256(rights_evidence_bytes).hexdigest()
    if actual_article_sha != declared_article_sha:
        raise KnowledgeRejected("ARTICLE_BYTES_DIGEST_MISMATCH")
    if actual_rights_sha != declared_rights_sha:
        raise KnowledgeRejected("RIGHTS_EVIDENCE_BYTES_DIGEST_MISMATCH")
    taxonomy, frozen_taxonomy = _validated_taxonomy(taxonomy_manifest)
    refs = item["taxonomy_refs"]
    if (type(refs) is not list or
            not 1 <= len(refs) <= MAX_TAXONOMY_ASSIGNMENTS):
        raise KnowledgeRejected("BOUNDED_CATEGORY_ASSIGNMENTS_REQUIRED")
    normalized_refs = [_ref(ref, CATEGORY_REF) for ref in refs]
    if len(set(normalized_refs)) != len(normalized_refs):
        raise KnowledgeRejected("DUPLICATE_CATEGORY_ASSIGNMENT")
    if any(ref not in taxonomy for ref in normalized_refs):
        raise KnowledgeRejected("UNKNOWN_TAXONOMY_CATEGORY")
    package_body = {
        "source_ref": source_ref,
        "title": title,
        "publisher": publisher,
        "canonical_url": canonical_url,
        "license_label_unverified": license_label,
        "rights_evidence_ref": rights_ref,
        "article_sha256": actual_article_sha,
        "rights_evidence_sha256": actual_rights_sha,
        "taxonomy_sha256": frozen_taxonomy["taxonomy_sha256"],
        "taxonomy_refs": sorted(normalized_refs),
        "intake_ref": intake_ref,
    }
    return {
        "status": "SOURCE_PACKAGE_INTEGRITY_VERIFIED_ONLY",
        "package_sha256": _digest(package_body),
        **package_body,
        "source_bytes_integrity_verified": True,
        "rights_evidence_bytes_integrity_verified": True,
        "verifier_persisted_source_bytes": False,
        "verifier_persisted_rights_evidence_bytes": False,
        "rights_verified": False,
        "license_semantics_reviewed": False,
        "scientifically_reviewed": False,
        "taxonomy_reviewed": False,
        "training_allowed": False,
        "clinical_use_allowed": False,
        "production_allowed": False,
    }
