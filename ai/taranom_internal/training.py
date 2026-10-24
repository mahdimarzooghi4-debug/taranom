"""TRN-TECH-143: executable one-feature least-squares learner, synthetic SHADOW only.

Training uses exact frozen TRAINING source refs; held-out benchmark reads only
INDEPENDENT_EVALUATION refs. This baseline engineering algorithm is NOT a
clinical model selection, inference/runtime, real consent or release approval.
"""
from decimal import Decimal, localcontext
import json
from .registry import OfflineRegistry,RegistryRejected,_ref,_mapping,_hash,_canonical,_metric_number,_open,MAX_OBSERVATIONS

ALGORITHM="TEST_ONLY_UNIVARIATE_OLS_V1"
DDL="""
CREATE TABLE IF NOT EXISTS shadow_training_runs(
 tenant TEXT NOT NULL,training_ref TEXT NOT NULL,request_ref TEXT NOT NULL,
 model_ref TEXT NOT NULL,version_ref TEXT NOT NULL,dataset_ref TEXT NOT NULL,
 input_sha TEXT NOT NULL,artifact_sha TEXT NOT NULL,artifact_json TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status='SYNTHETIC_SHADOW_ONLY'),
 PRIMARY KEY(tenant,training_ref),UNIQUE(tenant,request_ref),
 FOREIGN KEY(tenant,model_ref,version_ref)
 REFERENCES model_versions(tenant,model_ref,version_ref));
CREATE TABLE IF NOT EXISTS shadow_benchmarks(
 tenant TEXT NOT NULL,benchmark_ref TEXT NOT NULL,request_ref TEXT NOT NULL,
 training_ref TEXT NOT NULL,input_sha TEXT NOT NULL,
 baseline_mse TEXT NOT NULL,shadow_mse TEXT NOT NULL,
 baseline_mae TEXT NOT NULL,shadow_mae TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status='SYNTHETIC_DIAGNOSTICS_ONLY'),
 PRIMARY KEY(tenant,benchmark_ref),UNIQUE(tenant,request_ref),
 FOREIGN KEY(tenant,training_ref) REFERENCES shadow_training_runs(tenant,training_ref));
"""
def bootstrap(registry):
    con=_open(registry.database)
    try:
        con.executescript(DDL)
        for name in ("shadow_training_runs","shadow_benchmarks"):
            con.executescript(f"""
            CREATE TRIGGER IF NOT EXISTS {name}_no_update
            BEFORE UPDATE ON {name} BEGIN SELECT RAISE(ABORT,'IMMUTABLE'); END;
            CREATE TRIGGER IF NOT EXISTS {name}_no_delete
            BEFORE DELETE ON {name} BEGIN SELECT RAISE(ABORT,'IMMUTABLE'); END;""")
    finally:con.close()

def sample_rows(samples):
    if type(samples) is not list or not 1<=len(samples)<=MAX_OBSERVATIONS:
        raise RegistryRejected("BOUNDED_SHADOW_SAMPLES_REQUIRED")
    records={}
    for item in samples:
        _mapping(item,("source_ref","feature","observed"))
        src=_ref(item["source_ref"])
        if src in records:raise RegistryRejected("DUPLICATE_SHADOW_SOURCE")
        records[src]=(_metric_number(item["feature"]),_metric_number(item["observed"]))
    rows=[(ref,*records[ref]) for ref in sorted(records)]
    evidence=[{"source_ref":ref,"feature":str(x),"observed":str(y)} for ref,x,y in rows]
    return rows,_hash(evidence)

def dataset(con,tenant,ref):
    row=con.execute("SELECT manifest_sha,manifest_json FROM datasets WHERE tenant=? AND version_ref=?",
                    (tenant,ref)).fetchone()
    if not row:raise RegistryRejected("FROZEN_DATASET_NOT_FOUND")
    parsed=json.loads(row[1])
    if row[0]!=_hash(parsed):raise RegistryRejected("MANIFEST_INTEGRITY_CHANGED")
    return row[0],parsed

def partition(manifest,category):
    return {s["source_ref"] for s in manifest["sources"] if s["partition"]==category}

def finite(value):
    if not value.is_finite() or abs(value)>Decimal("1e30"):
        raise RegistryRejected("NONFINITE_OR_UNBOUNDED_SHADOW_RESULT")
    if value == 0:
        return "0"
    return format(value,"f")

def fit(registry,claim):
    c=_mapping(claim,("tenant","training_ref","model_ref","version_ref",
                       "dataset_ref","request_ref","samples"))
    for field in ("tenant","training_ref","model_ref","version_ref","dataset_ref","request_ref"):
        _ref(c[field])
    rows,input_sha=sample_rows(c["samples"])
    if len(rows)<2:raise RegistryRejected("SHADOW_TRAINING_NEEDS_TWO_SAMPLES")
    with registry.transaction() as con:
        manifest_sha,manifest=dataset(con,c["tenant"],c["dataset_ref"])
        from .provenance import guard_manifest
        guard_manifest(con,c["tenant"],manifest)
        if {r[0] for r in rows}!=partition(manifest,"TRAINING"):
            raise RegistryRejected("TRAINING_PARTITION_MISMATCH")
        with localcontext() as ctx:
            ctx.prec=60
            n=Decimal(len(rows))
            mean_x=sum((x for _,x,_ in rows),Decimal(0))/n
            mean_y=sum((y for _,_,y in rows),Decimal(0))/n
            variance=sum(((x-mean_x)**2 for _,x,_ in rows),Decimal(0))
            if variance==0:raise RegistryRejected("DEGENERATE_FEATURE_VARIANCE")
            covariance=sum(((x-mean_x)*(y-mean_y) for _,x,y in rows),Decimal(0))
            slope=covariance/variance
            intercept=mean_y-slope*mean_x
            values=(finite(intercept),finite(slope),finite(mean_y))
        artifact={"contract":"TRN-TECH-143","algorithm":ALGORITHM,
                  "tenant":c["tenant"],"model_ref":c["model_ref"],
                  "version_ref":c["version_ref"],"dataset_ref":c["dataset_ref"],
                  "dataset_sha256":manifest_sha,"training_samples_sha256":input_sha,
                  "training_sources":[r[0] for r in rows],"intercept":values[0],
                  "slope":values[1],"training_mean":values[2]}
        artifact_sha=_hash(artifact)
        old=con.execute("""SELECT training_ref,model_ref,version_ref,dataset_ref,
            input_sha,artifact_sha FROM shadow_training_runs
            WHERE tenant=? AND request_ref=?""",(c["tenant"],c["request_ref"])).fetchone()
        if old:
            if old!=(c["training_ref"],c["model_ref"],c["version_ref"],
                     c["dataset_ref"],input_sha,artifact_sha):
                raise RegistryRejected("CONFLICTING_SHADOW_TRAIN_REPLAY")
            return training_result(c["training_ref"],artifact_sha,True)
        if con.execute("SELECT 1 FROM shadow_training_runs WHERE tenant=? AND training_ref=?",
                       (c["tenant"],c["training_ref"])).fetchone():
            raise RegistryRejected("TRAINING_REF_ALREADY_USED")
        if con.execute("""SELECT 1 FROM model_versions WHERE
            tenant=? AND model_ref=? AND version_ref=?""",
            (c["tenant"],c["model_ref"],c["version_ref"])).fetchone():
            raise RegistryRejected("MODEL_VERSION_ALREADY_REGISTERED")
        model={"tenant":c["tenant"],"model_ref":c["model_ref"],
               "version_ref":c["version_ref"],"dataset_ref":c["dataset_ref"],
               "adapter_ref":"test:offline-ols-v1","artifact_sha256":artifact_sha,
               "request_ref":c["request_ref"]}
        con.execute("INSERT INTO model_versions VALUES(?,?,?,?,?,?,?,?,?)",(
            model["tenant"],model["model_ref"],model["version_ref"],model["dataset_ref"],
            model["adapter_ref"],artifact_sha,model["request_ref"],
            _hash(model),"UNVERIFIED_OFFLINE"))
        con.execute("INSERT INTO shadow_training_runs VALUES(?,?,?,?,?,?,?,?,?,?)",(
            c["tenant"],c["training_ref"],c["request_ref"],c["model_ref"],
            c["version_ref"],c["dataset_ref"],input_sha,artifact_sha,
            _canonical(artifact),"SYNTHETIC_SHADOW_ONLY"))
        return training_result(c["training_ref"],artifact_sha,False)

def training_result(ref,sha,replay):
    return {"status":"REPLAY_SHADOW_ONLY" if replay else "SYNTHETIC_SHADOW_CANDIDATE",
            "training_ref":ref,"artifact_sha256":sha,"candidate_approved":False,
            "model_selected":False,"training_allowed_on_real_data":False,
            "production_allowed":False}

def compare(registry,claim):
    c=_mapping(claim,("tenant","benchmark_ref","training_ref","request_ref","samples"))
    for field in ("tenant","benchmark_ref","training_ref","request_ref"):_ref(c[field])
    rows,input_sha=sample_rows(c["samples"])
    with registry.transaction() as con:
        run=con.execute("""SELECT model_ref,version_ref,dataset_ref,artifact_sha,artifact_json
            FROM shadow_training_runs WHERE tenant=? AND training_ref=?""",
            (c["tenant"],c["training_ref"])).fetchone()
        if not run:raise RegistryRejected("SHADOW_TRAINING_NOT_FOUND")
        model_ref,version_ref,dataset_ref,digest,raw=run
        artifact=json.loads(raw)
        if _hash(artifact)!=digest:raise RegistryRejected("ARTIFACT_TAMPERING_DETECTED")
        manifest_sha,manifest=dataset(con,c["tenant"],dataset_ref)
        if artifact["dataset_sha256"]!=manifest_sha:
            raise RegistryRejected("STALE_SHADOW_DATASET")
        from .provenance import guard_manifest
        guard_manifest(con,c["tenant"],manifest)
        model=con.execute("""SELECT artifact_sha FROM model_versions
          WHERE tenant=? AND model_ref=? AND version_ref=?""",
          (c["tenant"],model_ref,version_ref)).fetchone()
        if not model or model[0]!=digest:
            raise RegistryRejected("SHADOW_MODEL_REGISTRY_MISMATCH")
        if {r[0] for r in rows}!=partition(manifest,"INDEPENDENT_EVALUATION"):
            raise RegistryRejected("HELD_OUT_PARTITION_MISMATCH")
        if {r[0] for r in rows}&set(artifact["training_sources"]):
            raise RegistryRejected("TRAINING_SOURCE_IN_EVALUATION")
        with localcontext() as ctx:
            ctx.prec=60
            slope=Decimal(artifact["slope"]); intercept=Decimal(artifact["intercept"])
            mean=Decimal(artifact["training_mean"]); size=Decimal(len(rows))
            errors=[(y-mean,y-(intercept+slope*x)) for _,x,y in rows]
            metrics=(finite(sum((a*a for a,_ in errors),Decimal(0))/size),
                     finite(sum((b*b for _,b in errors),Decimal(0))/size),
                     finite(sum((abs(a) for a,_ in errors),Decimal(0))/size),
                     finite(sum((abs(b) for _,b in errors),Decimal(0))/size))
        old=con.execute("""SELECT benchmark_ref,training_ref,input_sha,
            baseline_mse,shadow_mse,baseline_mae,shadow_mae
            FROM shadow_benchmarks WHERE tenant=? AND request_ref=?""",
            (c["tenant"],c["request_ref"])).fetchone()
        if old:
            if old[:3]!=(c["benchmark_ref"],c["training_ref"],input_sha):
                raise RegistryRejected("CONFLICTING_BENCHMARK_REPLAY")
            return compare_result(c["benchmark_ref"],old[3:],True)
        if con.execute("SELECT 1 FROM shadow_benchmarks WHERE tenant=? AND benchmark_ref=?",
                       (c["tenant"],c["benchmark_ref"])).fetchone():
            raise RegistryRejected("BENCHMARK_REF_ALREADY_USED")
        con.execute("INSERT INTO shadow_benchmarks VALUES(?,?,?,?,?,?,?,?,?,?)",(
            c["tenant"],c["benchmark_ref"],c["request_ref"],c["training_ref"],
            input_sha,*metrics,"SYNTHETIC_DIAGNOSTICS_ONLY"))
        return compare_result(c["benchmark_ref"],metrics,False)

def compare_result(ref,metrics,replay):
    return {"status":"REPLAY_SHADOW_DIAGNOSTIC" if replay else "SYNTHETIC_SHADOW_BENCHMARK",
            "benchmark_ref":ref,"baseline_mse":metrics[0],"shadow_mse":metrics[1],
            "baseline_mae":metrics[2],"shadow_mae":metrics[3],
            "winner_selected":False,"threshold_defined":False,
            "clinical_evaluation":False,"human_approved":False,"production_allowed":False}

def inventory(registry,tenant):
    tenant=_ref(tenant)
    with registry.transaction() as con:
        runs=[{"training_ref":a,"model_ref":b,"version_ref":c,"artifact_sha256":d,
               "status":e,"candidate_approved":False}
          for a,b,c,d,e in con.execute("""SELECT training_ref,model_ref,version_ref,
            artifact_sha,status FROM shadow_training_runs WHERE tenant=?
            ORDER BY training_ref""",(tenant,))]
        benchmarks=[{"benchmark_ref":a,"training_ref":b,"baseline_mse":c,
                     "shadow_mse":d,"baseline_mae":e,"shadow_mae":f,
                     "status":g,"winner_selected":False}
          for a,b,c,d,e,f,g in con.execute("""SELECT benchmark_ref,training_ref,
            baseline_mse,shadow_mse,baseline_mae,shadow_mae,status
            FROM shadow_benchmarks WHERE tenant=? ORDER BY benchmark_ref""",(tenant,))]
        return {"training_runs":runs,"shadow_benchmarks":benchmarks}
