"""Synthetic-only internal AI registry, frozen dataset manifests and offline evaluation.

NO clinical or member payload, inference, training, model loading, production
promotion, real consent verification or external AI API. A versioned structural
backend, designed to be replaced with approved governance/persistence adapters.
"""
from __future__ import annotations

from contextlib import contextmanager
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation, localcontext
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import tempfile

REF = re.compile(r"test:[A-Za-z0-9_:-]{1,123}\Z", re.ASCII)
DIGEST = re.compile(r"[A-F0-9]{64}\Z", re.ASCII)
PARTITIONS = frozenset(("TRAINING", "INDEPENDENT_EVALUATION"))
OFFLINE = "CI_EPHEMERAL_ONLY"
MAX_SOURCES = 64
MAX_OBSERVATIONS = 64

class RegistryRejected(ValueError):
    """Fail closed on invalid input, missing lineage or attempted authority."""

def _ref(x):
    if type(x) is not str or not REF.fullmatch(x) or len(x)>128:
        raise RegistryRejected("SYNTHETIC_TEST_REF_REQUIRED")
    return x

def _digest(x):
    if type(x) is not str or not DIGEST.fullmatch(x):
        raise RegistryRejected("UPPERCASE_SHA256_REQUIRED")
    return x

def _mapping(raw,keys):
    if type(raw) is not dict or set(raw)!=set(keys):
        raise RegistryRejected("EXACT_METADATA_SCHEMA_REQUIRED")
    return raw

def _canonical(value):
    return json.dumps(value,sort_keys=True,separators=(",",":"),ensure_ascii=True)

def _hash(value):
    return hashlib.sha256(_canonical(value).encode("ascii")).hexdigest().upper()

def _claims(sources):
    if type(sources) is not list or not 2<=len(sources)<=MAX_SOURCES:
        raise RegistryRejected("BOTH_PARTITIONS_REQUIRED")
    accepted=[]
    seen=set()
    members={"TRAINING":set(),"INDEPENDENT_EVALUATION":set()}
    for s in sources:
        _mapping(s,("source_ref","subject_ref","partition","source_sha256",
                   "claimed_consent_ref","claimed_rights_review_ref"))
        src=_ref(s["source_ref"])
        subject=_ref(s["subject_ref"])
        if s["partition"] not in PARTITIONS or type(s["partition"]) is not str:
            raise RegistryRejected("INVALID_PARTITION")
        _digest(s["source_sha256"])
        _ref(s["claimed_consent_ref"])
        _ref(s["claimed_rights_review_ref"])
        if src in seen:
            raise RegistryRejected("SOURCE_OVERLAP_OR_DUPLICATE")
        seen.add(src)
        members[s["partition"]].add(subject)
        accepted.append(dict(s))
    if not all(members.values()):
        raise RegistryRejected("BOTH_PARTITIONS_REQUIRED")
    if members["TRAINING"] & members["INDEPENDENT_EVALUATION"]:
        raise RegistryRejected("SUBJECT_OVERLAP_TRAINING_EVALUATION")
    return sorted(accepted,key=lambda x:x["source_ref"])

def _metric_number(value):
    if type(value) is not str or len(value)>64:
        raise RegistryRejected("DECIMAL_STRING_REQUIRED")
    try:
        d=Decimal(value)
    except InvalidOperation as exc:
        raise RegistryRejected("INVALID_NUMERIC_OBSERVATION") from exc
    if not d.is_finite() or abs(d)>Decimal("1000000000"):
        raise RegistryRejected("UNBOUNDED_NUMERIC_OBSERVATION")
    return d

def _open(database):
    if os.environ.get("TARANOM_AI_OFFLINE_TEST_ONLY")!=OFFLINE:
        raise RegistryRejected("EPHEMERAL_AI_TEST_MODE_REQUIRED")
    path=Path(database)
    if not path.is_absolute() or path.suffix!=".sqlite3":
        raise RegistryRejected("EXPLICIT_EPHEMERAL_SQLITE_PATH_REQUIRED")
    # No arbitrary real production DB path may ever be opened.
    base=Path(tempfile.gettempdir()).resolve()
    if path.is_symlink() or not path.resolve().is_relative_to(base) or path==base:
        raise RegistryRejected("UNTRUSTED_AI_STORAGE_PATH")
    if path.exists() and not path.is_file():
        raise RegistryRejected("UNTRUSTED_AI_STORAGE_PATH")
    path.parent.mkdir(parents=True,exist_ok=True)
    con=sqlite3.connect(str(path),timeout=15,isolation_level=None)
    try:
        con.execute("PRAGMA foreign_keys=ON")
        con.execute("PRAGMA busy_timeout=15000")
        con.execute("PRAGMA journal_mode=WAL")
        return con
    except BaseException:
        con.close()
        raise

SCHEMA = """
CREATE TABLE IF NOT EXISTS datasets (
 tenant TEXT NOT NULL, version_ref TEXT NOT NULL, purpose_ref TEXT NOT NULL,
 category_ref TEXT NOT NULL, manifest_sha TEXT NOT NULL,
 request_ref TEXT NOT NULL, manifest_json TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status='UNVERIFIED_SYNTHETIC'),
 PRIMARY KEY(tenant,version_ref), UNIQUE(tenant,request_ref)
);
CREATE TABLE IF NOT EXISTS model_versions (
 tenant TEXT NOT NULL, model_ref TEXT NOT NULL, version_ref TEXT NOT NULL,
 dataset_ref TEXT NOT NULL, adapter_ref TEXT NOT NULL, artifact_sha TEXT NOT NULL,
 request_ref TEXT NOT NULL, fingerprint TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status='UNVERIFIED_OFFLINE'),
 PRIMARY KEY(tenant,model_ref,version_ref), UNIQUE(tenant,request_ref),
 FOREIGN KEY(tenant,dataset_ref) REFERENCES datasets(tenant,version_ref)
);
CREATE TABLE IF NOT EXISTS evaluations (
 tenant TEXT NOT NULL, evaluation_ref TEXT NOT NULL,
 model_ref TEXT NOT NULL, version_ref TEXT NOT NULL, dataset_ref TEXT NOT NULL,
 request_ref TEXT NOT NULL, observations_sha TEXT NOT NULL,
 mse TEXT NOT NULL, mae TEXT NOT NULL, evidence_json TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status='SYNTHETIC_DIAGNOSTIC_ONLY'),
 PRIMARY KEY(tenant,evaluation_ref), UNIQUE(tenant,request_ref),
 FOREIGN KEY(tenant,model_ref,version_ref)
   REFERENCES model_versions(tenant,model_ref,version_ref)
);
"""
forbidden_update="UPDATE"  # Used only to name immutable triggers; never modify accepted rows.

@dataclass(frozen=True)
class OfflineRegistry:
    database: str

    @contextmanager
    def transaction(self):
        con=_open(self.database)
        try:
            con.execute("BEGIN IMMEDIATE")
            yield con
            con.commit()
        except BaseException:
            con.rollback()
            raise
        finally:
            con.close()

    def initialize(self):
        con=_open(self.database)
        try:
            con.executescript(SCHEMA)
            for table in ("datasets","model_versions","evaluations"):
                con.executescript(f"""
                  CREATE TRIGGER IF NOT EXISTS no_{table}_update
                    BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'IMMUTABLE'); END;
                  CREATE TRIGGER IF NOT EXISTS no_{table}_delete
                    BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'IMMUTABLE'); END;
                """)
        finally:
            con.close()

    def freeze_dataset(self,claim):
        c=_mapping(claim,("tenant","version_ref","purpose_ref","category_ref",
                          "request_ref","sources"))
        for key in ("tenant","version_ref","purpose_ref","category_ref","request_ref"):
            _ref(c[key])
        sources=_claims(c["sources"])
        snapshot={k:c[k] for k in ("tenant","version_ref","purpose_ref","category_ref")}
        snapshot["sources"]=sources
        digest=_hash(snapshot)
        with self.transaction() as con:
            existing=con.execute("SELECT version_ref,manifest_sha FROM datasets WHERE tenant=? AND request_ref=?",
                (c["tenant"],c["request_ref"])).fetchone()
            if existing:
                if existing!=(c["version_ref"],digest):
                    raise RegistryRejected("CONFLICTING_DATASET_REQUEST_REPLAY")
                return {"status":"REPLAY_UNVERIFIED","manifest_sha256":digest,"version_ref":c["version_ref"],
                        "training_allowed":False,"production_allowed":False}
            if con.execute("SELECT 1 FROM datasets WHERE tenant=? AND version_ref=?",
                           (c["tenant"],c["version_ref"])).fetchone():
                raise RegistryRejected("DATASET_VERSION_ALREADY_FROZEN")
            con.execute("""INSERT INTO datasets VALUES (?,?,?,?,?,?,?,?)""",
                        (c["tenant"],c["version_ref"],c["purpose_ref"],c["category_ref"],
                         digest,c["request_ref"],_canonical(snapshot),"UNVERIFIED_SYNTHETIC"))
        return {"status":"FROZEN_UNVERIFIED","manifest_sha256":digest,"version_ref":c["version_ref"],
                "training_allowed":False,"production_allowed":False}

    def register_model(self,claim):
        c=_mapping(claim,("tenant","model_ref","version_ref","dataset_ref",
                          "adapter_ref","artifact_sha256","request_ref"))
        for key in ("tenant","model_ref","version_ref","dataset_ref","adapter_ref","request_ref"):
            _ref(c[key])
        _digest(c["artifact_sha256"])
        # A declared adapter is metadata, NOT a loaded/supported runtime.
        fingerprint=_hash({k:c[k] for k in sorted(c)})
        with self.transaction() as con:
            if not con.execute("SELECT 1 FROM datasets WHERE tenant=? AND version_ref=?",
                               (c["tenant"],c["dataset_ref"])).fetchone():
                raise RegistryRejected("FROZEN_DATASET_NOT_FOUND")
            old=con.execute("""SELECT model_ref,version_ref,fingerprint
                FROM model_versions WHERE tenant=? AND request_ref=?""",
                (c["tenant"],c["request_ref"])).fetchone()
            if old:
                if old!=(c["model_ref"],c["version_ref"],fingerprint):
                    raise RegistryRejected("CONFLICTING_MODEL_REQUEST_REPLAY")
                return self._model_result(c["model_ref"],c["version_ref"],True)
            if con.execute("""SELECT 1 FROM model_versions
                WHERE tenant=? AND model_ref=? AND version_ref=?""",
                (c["tenant"],c["model_ref"],c["version_ref"])).fetchone():
                raise RegistryRejected("MODEL_VERSION_ALREADY_REGISTERED")
            con.execute("""INSERT INTO model_versions VALUES (?,?,?,?,?,?,?,?,?)""",
                        (c["tenant"],c["model_ref"],c["version_ref"],c["dataset_ref"],
                         c["adapter_ref"],c["artifact_sha256"],c["request_ref"],
                         fingerprint,"UNVERIFIED_OFFLINE"))
        return self._model_result(c["model_ref"],c["version_ref"],False)

    @staticmethod
    def _model_result(model,version,replay):
        return {"status":"REPLAY_UNVERIFIED" if replay else "REGISTERED_UNVERIFIED",
                "model_ref":model,"version_ref":version,
                "inference_supported":False,"training_allowed":False,
                "production_allowed":False}

    def evaluate_synthetic(self,claim):
        c=_mapping(claim,("tenant","evaluation_ref","model_ref","version_ref",
                          "dataset_ref","request_ref","samples"))
        for key in ("tenant","evaluation_ref","model_ref","version_ref","dataset_ref","request_ref"):
            _ref(c[key])
        if type(c["samples"]) is not list or not 1<=len(c["samples"])<=MAX_OBSERVATIONS:
            raise RegistryRejected("SYNTHETIC_EVALUATION_SAMPLES_REQUIRED")
        rows=[]
        seen=set()
        for entry in c["samples"]:
            _mapping(entry,("source_ref","observed","predicted"))
            source=_ref(entry["source_ref"])
            if source in seen:
                raise RegistryRejected("DUPLICATE_EVALUATION_SOURCE")
            seen.add(source)
            observed=_metric_number(entry["observed"])
            predicted=_metric_number(entry["predicted"])
            rows.append((source,observed,predicted))
        rows.sort(key=lambda x:x[0])
        evidence=[{"source_ref":s,"observed":str(o),"predicted":str(p)}
                  for s,o,p in rows]
        fingerprint=_hash(evidence)
        with self.transaction() as con:
            model=con.execute("""SELECT dataset_ref FROM model_versions
                WHERE tenant=? AND model_ref=? AND version_ref=?""",
                (c["tenant"],c["model_ref"],c["version_ref"])).fetchone()
            if not model or model[0]!=c["dataset_ref"]:
                raise RegistryRejected("MODEL_DATASET_LINEAGE_MISMATCH")
            dataset=con.execute("""SELECT manifest_json FROM datasets
                WHERE tenant=? AND version_ref=?""",
                (c["tenant"],c["dataset_ref"])).fetchone()
            if not dataset:
                raise RegistryRejected("DATASET_NOT_FOUND")
            manifest=json.loads(dataset[0])
            expected={s["source_ref"] for s in manifest["sources"]
                      if s["partition"]=="INDEPENDENT_EVALUATION"}
            if set(seen)!=expected:
                raise RegistryRejected("INDEPENDENT_EVALUATION_PARTITION_MISMATCH")
            old=con.execute("""SELECT evaluation_ref,model_ref,version_ref,
                dataset_ref,observations_sha,mse,mae FROM evaluations
                WHERE tenant=? AND request_ref=?""",
                (c["tenant"],c["request_ref"])).fetchone()
            if old:
                if old[:5]!=(c["evaluation_ref"],c["model_ref"],c["version_ref"],
                            c["dataset_ref"],fingerprint):
                    raise RegistryRejected("CONFLICTING_EVALUATION_REQUEST_REPLAY")
                return self._eval_result(c["evaluation_ref"],old[5],old[6],True)
            if con.execute("SELECT 1 FROM evaluations WHERE tenant=? AND evaluation_ref=?",
                           (c["tenant"],c["evaluation_ref"])).fetchone():
                raise RegistryRejected("EVALUATION_VERSION_ALREADY_RECORDED")
            with localcontext() as context:
                context.prec=80
                errors=[o-p for _,o,p in rows]
                mse=sum((x*x for x in errors),Decimal(0))/len(errors)
                mae=sum((abs(x) for x in errors),Decimal(0))/len(errors)
                mse_s=format(mse,'f')
                mae_s=format(mae,'f')
            con.execute("""INSERT INTO evaluations VALUES (?,?,?,?,?,?,?,?,?,?,?)""",
                        (c["tenant"],c["evaluation_ref"],c["model_ref"],c["version_ref"],
                         c["dataset_ref"],c["request_ref"],fingerprint,
                         mse_s,mae_s,_canonical(evidence),"SYNTHETIC_DIAGNOSTIC_ONLY"))
        return self._eval_result(c["evaluation_ref"],mse_s,mae_s,False)

    @staticmethod
    def _eval_result(ref,mse,mae,replay):
        return {"status":"REPLAY_SYNTHETIC_ONLY" if replay else "MEASURED_SYNTHETIC_ONLY",
                "evaluation_ref":ref,"mse":mse,"mae":mae,
                "candidate_approved":False,"production_allowed":False}

    def read_inventory(self,tenant):
        tenant=_ref(tenant)
        with self.transaction() as con:
            datasets=[{"version_ref":v,"manifest_sha256":h,"status":s,
                       "source_count":len(json.loads(j)["sources"])}
                      for v,h,s,j in con.execute("""SELECT version_ref,manifest_sha,
                         status,manifest_json FROM datasets WHERE tenant=? ORDER BY version_ref""",
                         (tenant,))]
            models=[{"model_ref":m,"version_ref":v,"dataset_ref":d,"adapter_ref":a,
                     "artifact_sha256":h,"status":s,"inference_supported":False}
                    for m,v,d,a,h,s in con.execute("""SELECT model_ref,version_ref,
                        dataset_ref,adapter_ref,artifact_sha,status FROM model_versions
                        WHERE tenant=? ORDER BY model_ref,version_ref""",(tenant,))]
            evaluations=[{"evaluation_ref":e,"model_ref":m,"version_ref":v,
                          "mse":mse,"mae":mae,"status":s}
                         for e,m,v,mse,mae,s in con.execute("""SELECT evaluation_ref,
                           model_ref,version_ref,mse,mae,status
                           FROM evaluations WHERE tenant=? ORDER BY evaluation_ref""",
                           (tenant,))]
        return {"tenant":tenant,"datasets":datasets,"model_versions":models,
                "evaluations":evaluations,"stage_admission":"DENIED",
                "production_allowed":False,"training_allowed":False,
                "real_data_eligible":False}
