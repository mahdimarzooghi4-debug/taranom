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
    text = text.replace("\u200c", " ").replace("\u200d", " ")
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
