"""TRN-TECH-145: synthetic-only immutable provenance claims and source retirement.

Events record *unverified declared* refs only, never actual consent or source
rights. Retirement blocks future synthetic freeze, model evaluation and
training/benchmark, including exact replay. This is NOT a lawful data gateway.
"""
from __future__ import annotations
import json
from .registry import OfflineRegistry,RegistryRejected,_ref,_digest,_mapping,_hash,_canonical,_open

DDL="""
CREATE TABLE IF NOT EXISTS synthetic_source_events(
  tenant TEXT NOT NULL,
  seq INTEGER NOT NULL CHECK(seq>=1),
  request_ref TEXT NOT NULL,
  source_ref TEXT NOT NULL,
  action TEXT NOT NULL CHECK(action IN ('CLAIM_RECORDED','SOURCE_RETIRED')),
  payload_json TEXT NOT NULL,
  previous_sha TEXT NOT NULL,
  event_sha TEXT NOT NULL,
  PRIMARY KEY(tenant,seq),
  UNIQUE(tenant,request_ref)
);
"""
CLAIM_FIELDS=("source_ref","subject_ref","partition","source_sha256",
              "claimed_consent_ref","claimed_rights_review_ref")
NO_PREVIOUS="0"*64

def bootstrap(registry:OfflineRegistry):
    conn=_open(registry.database)
    try:
        conn.executescript(DDL)
        for suffix,action in (("update","UPDATE"),("delete","DELETE")):
            conn.executescript(f"""
                CREATE TRIGGER IF NOT EXISTS source_events_no_{suffix}
                BEFORE {action} ON synthetic_source_events
                BEGIN SELECT RAISE(ABORT,'SOURCE_EVENTS_APPEND_ONLY'); END;
            """)
    finally:conn.close()

def _event_payload(tenant,seq,request_ref,source_ref,action,payload,previous):
    return {"tenant":tenant,"seq":seq,"request_ref":request_ref,
            "source_ref":source_ref,"action":action,
            "payload":payload,"previous_sha256":previous}

def states(conn,tenant):
    tenant=_ref(tenant)
    events=conn.execute("""SELECT seq,request_ref,source_ref,action,
        payload_json,previous_sha,event_sha FROM synthetic_source_events
        WHERE tenant=? ORDER BY seq""",(tenant,)).fetchall()
    source={}
    previous=NO_PREVIOUS
    for expected,event in enumerate(events,1):
        seq,request,ref,action,raw,prev,digest=event
        if seq!=expected or previous!=prev:
            raise RegistryRejected("PROVENANCE_EVENT_CHAIN_BROKEN")
        try:
            payload=json.loads(raw)
        except (TypeError,ValueError) as exc:
            raise RegistryRejected("PROVENANCE_EVENT_PAYLOAD_CORRUPTED") from exc
        if _hash(_event_payload(tenant,seq,request,ref,action,payload,prev))!=digest:
            raise RegistryRejected("PROVENANCE_EVENT_HASH_MISMATCH")
        if action=="CLAIM_RECORDED":
            if ref in source or type(payload) is not dict or set(payload)!=set(CLAIM_FIELDS):
                raise RegistryRejected("PROVENANCE_DUPLICATE_OR_INVALID_CLAIM")
            source[ref]={"claim":payload,"retired":False}
        elif action=="SOURCE_RETIRED":
            if ref not in source or source[ref]["retired"] or type(payload) is not dict or set(payload)!={"reason_ref"}:
                raise RegistryRejected("PROVENANCE_RETIRE_SEQUENCE_INVALID")
            source[ref]["retired"]=True
        else:
            raise RegistryRejected("PROVENANCE_UNKNOWN_ACTION")
        previous=digest
    return source,(len(events),previous)

def _append(conn,tenant,request,ref,action,payload,position):
    seq,previous=position
    seq+=1
    event_sha=_hash(_event_payload(tenant,seq,request,ref,action,payload,previous))
    conn.execute("""INSERT INTO synthetic_source_events
        (tenant,seq,request_ref,source_ref,action,payload_json,previous_sha,event_sha)
        VALUES (?,?,?,?,?,?,?,?)""",(tenant,seq,request,ref,action,
            _canonical(payload),previous,event_sha))
    return event_sha

def _claim_data(raw):
    _mapping(raw,("tenant","request_ref","source"))
    tenant=_ref(raw["tenant"]);request=_ref(raw["request_ref"])
    claim=_mapping(raw["source"],CLAIM_FIELDS)
    from .registry import PARTITIONS
    for field in ("source_ref","subject_ref","claimed_consent_ref","claimed_rights_review_ref"):
        _ref(claim[field])
    _digest(claim["source_sha256"])
    if type(claim["partition"]) is not str or claim["partition"] not in PARTITIONS:
        raise RegistryRejected("PROVENANCE_PARTITION_INVALID")
    return tenant,request,dict(claim)

def register(registry:OfflineRegistry,raw):
    tenant,request,claim=_claim_data(raw)
    ref=claim["source_ref"]
    with registry.transaction() as conn:
        found,position=states(conn,tenant)
        old=conn.execute("""SELECT action,source_ref,payload_json
            FROM synthetic_source_events WHERE tenant=? AND request_ref=?""",
            (tenant,request)).fetchone()
        if old:
            if old!=("CLAIM_RECORDED",ref,_canonical(claim)):
                raise RegistryRejected("CONFLICTING_SOURCE_CLAIM_REPLAY")
            return {"status":"REPLAY_UNVERIFIED_CLAIM","source_ref":ref,
                    "source_retired":found[ref]["retired"],
                    "rights_verified":False,"production_allowed":False}
        if ref in found:
            raise RegistryRejected("SOURCE_CLAIM_ALREADY_REGISTERED")
        _append(conn,tenant,request,ref,"CLAIM_RECORDED",claim,position)
    return {"status":"UNVERIFIED_CLAIM_RECORDED","source_ref":ref,
            "source_retired":False,"rights_verified":False,
            "production_allowed":False}

def retire(registry:OfflineRegistry,raw):
    _mapping(raw,("tenant","request_ref","source_ref","reason_ref"))
    tenant=_ref(raw["tenant"]);request=_ref(raw["request_ref"])
    source=_ref(raw["source_ref"]);reason=_ref(raw["reason_ref"])
    payload={"reason_ref":reason}
    with registry.transaction() as conn:
        found,position=states(conn,tenant)
        old=conn.execute("""SELECT action,source_ref,payload_json
            FROM synthetic_source_events WHERE tenant=? AND request_ref=?""",
            (tenant,request)).fetchone()
        if old:
            if old!=("SOURCE_RETIRED",source,_canonical(payload)):
                raise RegistryRejected("CONFLICTING_SOURCE_RETIRE_REPLAY")
            return {"status":"REPLAY_RETIRED_TEST_SOURCE","source_ref":source,
                    "training_blocked":True,"rights_verified":False,
                    "production_allowed":False}
        if source not in found:
            raise RegistryRejected("SOURCE_CLAIM_NOT_FOUND")
        if found[source]["retired"]:
            raise RegistryRejected("SOURCE_ALREADY_RETIRED")
        _append(conn,tenant,request,source,"SOURCE_RETIRED",payload,position)
    return {"status":"TEST_SOURCE_RETIRED","source_ref":source,
            "training_blocked":True,"rights_verified":False,
            "production_allowed":False}

def guard_manifest(conn,tenant,manifest):
    """Transitionally supports old test-only manifests when no source is tracked.

    Once any source is tracked for a tenant, ALL sources of that tenant's
    manifest must match registered claims; any retirement blocks even replay.
    This NEVER returns real-data eligibility or legally verified consent.
    """
    tracked,_=states(conn,tenant)
    manifest_sources=manifest["sources"]
    if tracked:
        for entry in manifest_sources:
            ref=entry["source_ref"]
            if ref not in tracked:
                raise RegistryRejected("PROVENANCE_SOURCE_CLAIM_MISSING")
            row=tracked[ref]
            if row["retired"]:
                raise RegistryRejected("PROVENANCE_SOURCE_RETIRED")
            if row["claim"]!=entry:
                raise RegistryRejected("PROVENANCE_FROZEN_CLAIM_MISMATCH")
    return {"technical_claims_checked":bool(tracked),
            "legal_consent_verified":False,"rights_verified":False,
            "real_training_allowed":False}

def read_summary(registry:OfflineRegistry,tenant):
    tenant=_ref(tenant)
    with registry.transaction() as conn:
        known,position=states(conn,tenant)
    return {"tenant":tenant,"recorded_test_source_count":len(known),
            "retired_test_source_count":sum(v["retired"] for v in known.values()),
            "latest_event_sha256":position[1],
            "status":"UNVERIFIED_SYNTHETIC_CLAIMS_ONLY",
            "legal_consent_verified":False,"real_data_eligible":False,
            "production_allowed":False}
