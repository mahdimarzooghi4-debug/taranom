import {useEffect,useState} from 'react';

type Row = Record<string,unknown>;
type Inventory = {tenant:string;datasets:Row[];model_versions:Row[];evaluations:Row[];
  stage_admission:'DENIED';production_allowed:false;training_allowed:false;real_data_eligible:false};
type Envelope = {contract:'TRN-TECH-142';outcome:string;syntheticOnly:true;
  adminAuthenticated:false;operatorAuthorized:false;trainingAllowed:false;
  inferenceSupported:false;stageAllowed:false;productionAllowed:false;data:unknown};
const ref=/^test:[A-Za-z0-9_:-]{1,123}$/;
const digest='A'.repeat(64);
function envelope(value:unknown):value is Envelope {
 if(!value||typeof value!=='object'||Array.isArray(value))return false;
 const x=value as Partial<Envelope>;
 return x.contract==='TRN-TECH-142'&&x.syntheticOnly===true&&
  x.adminAuthenticated===false&&x.operatorAuthorized===false&&
  x.trainingAllowed===false&&x.inferenceSupported===false&&
  x.stageAllowed===false&&x.productionAllowed===false&&
  typeof x.outcome==='string';
}
function validInventory(raw:unknown,tenant:string):raw is Inventory {
 if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
 const x=raw as Partial<Inventory>;
 if(x.tenant!==tenant||x.stage_admission!=='DENIED'||
  x.production_allowed!==false||x.training_allowed!==false||
  x.real_data_eligible!==false||!Array.isArray(x.datasets)||
  !Array.isArray(x.model_versions)||!Array.isArray(x.evaluations))return false;
 return x.datasets.every(y=>y&&y.status==='UNVERIFIED_SYNTHETIC'&&
   typeof y.version_ref==='string'&&ref.test(y.version_ref))&&
  x.model_versions.every(y=>y&&y.status==='UNVERIFIED_OFFLINE'&&
   y.inference_supported===false&&typeof y.model_ref==='string')&&
  x.evaluations.every(y=>y&&y.status==='SYNTHETIC_DIAGNOSTIC_ONLY'&&
   typeof y.evaluation_ref==='string');
}
const base='/internal/ai-test/v1';
export function OfflineAiQaApp(){
 const [tenant,setTenant]=useState('test:qa-tenant');
 const [dataset,setDataset]=useState('test:qa-dataset-v1');
 const [model,setModel]=useState('test:qa-model');
 const [version,setVersion]=useState('test:qa-model-v1');
 const [evaluation,setEvaluation]=useState('test:qa-evaluation-v1');
 const [predicted,setPredicted]=useState('1.5');
 const [rows,setRows]=useState<Inventory|null>(null);
 const [feedback,setFeedback]=useState('هنوز داده‌ای از Backend خوانده نشده است.');
 const [busy,setBusy]=useState(false);
 const acceptable=[tenant,dataset,model,version,evaluation].every(x=>ref.test(x));
 async function call(path:string,body?:unknown){
  const response=await fetch(base+path,{
   method:body?'POST':'GET',credentials:'omit',cache:'no-store',
   ...(body?{headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}:{})
  });
  const value:unknown=await response.json();
  if(!envelope(value))throw Error('UNVERIFIED_AUTHORITY');
  if(!response.ok)throw Error(value.outcome);
  return value.data;
 }
 async function read(){
  if(!ref.test(tenant)||busy)return;
  setBusy(true);setRows(null);
  try{
   const value=await call('/inventory?tenant='+encodeURIComponent(tenant));
   if(!validInventory(value,tenant))throw Error('UNVERIFIED_INVENTORY');
   setRows(value);
   setFeedback('اطلاعات نسخه‌دار آزمایشی از SQLite خوانده شد؛ هیچ مدل فعالی وجود ندارد.');
  }catch{setFeedback('اتصال یا داده نامعتبر است؛ هیچ اطلاعاتی تأیید نشد.');}
  finally{setBusy(false);}
 }
 useEffect(()=>{void read();/* initial read is test-only, not a login */},[]);
 async function submit(kind:'dataset'|'model'|'eval'){
  if(!acceptable||busy||!Number.isFinite(Number(predicted)))return;
  setBusy(true);setRows(null);
  const common={tenant,request_ref:kind==='dataset'?'test:freeze-'+dataset.slice(5):
    kind==='model'?'test:register-'+version.slice(5):'test:evaluate-'+evaluation.slice(5)};
  const source=(part:'TRAINING'|'INDEPENDENT_EVALUATION')=>({
   source_ref:part==='TRAINING'?'test:qa-source-train':'test:qa-source-eval',
   subject_ref:part==='TRAINING'?'test:qa-subject-train':'test:qa-subject-eval',
   partition:part,source_sha256:digest,claimed_consent_ref:'test:unverified-consent',
   claimed_rights_review_ref:'test:unverified-rights'
  });
  const body=kind==='dataset'?{...common,version_ref:dataset,purpose_ref:'test:research',
    category_ref:'test:metadata',sources:[source('TRAINING'),source('INDEPENDENT_EVALUATION')]}:
   kind==='model'?{...common,model_ref:model,version_ref:version,
    dataset_ref:dataset,adapter_ref:'test:unimplemented-adapter',artifact_sha256:digest}:
    {...common,evaluation_ref:evaluation,model_ref:model,version_ref:version,
     dataset_ref:dataset,samples:[{source_ref:'test:qa-source-eval',
       observed:'2.0',predicted:predicted.trim()}]};
  try{
   await call(kind==='dataset'?'/datasets':kind==='model'?'/models':'/evaluations',body);
   const data=await call('/inventory?tenant='+encodeURIComponent(tenant));
   if(!validInventory(data,tenant))throw Error('BAD_RESULT');
   setRows(data);
   setFeedback('شاهد فنی ساختگی ثبت شد. آموزش، استنتاج و ارتقای مدل همچنان ممنوع است.');
  }catch{setFeedback('درخواست رد شد یا شواهد قابل تأیید نیستند.');}
  finally{setBusy(false);}
 }
 return <>
  <a href="#ai-content" className="skip">پرش به محتوا</a>
  <header><p>ترنم | فضای آزمایش داخلی AI</p><h1>مدیریت آزمایشی Dataset و نسخه مدل</h1>
   <p>SQLite واقعی در Backend محلی؛ فاقد احراز هویت ادمین، داده سلامت، آموزش و استنتاج.</p></header>
  <main id="ai-content">
   <section className="notice" role="note"><strong>Production / Stage: DENIED</strong>
    <p>این صفحه فقط با مراجع test: کار می‌کند. ثبت نسخه هرگز به معنی تأیید مدل نیست.</p></section>
   <section className="panel">
    <h2>مشخصات نسخه‌های آزمایشی</h2>
    <div className="input-grid">
     <label>محدوده تست<input aria-label="محدوده تست" value={tenant} onChange={e=>{setTenant(e.target.value);setRows(null);}}/></label>
     <label>نسخه Dataset<input aria-label="نسخه Dataset" value={dataset} onChange={e=>setDataset(e.target.value)}/></label>
     <label>شناسه مدل<input aria-label="شناسه مدل" value={model} onChange={e=>setModel(e.target.value)}/></label>
     <label>نسخه مدل<input aria-label="نسخه مدل" value={version} onChange={e=>setVersion(e.target.value)}/></label>
     <label>شناسه ارزیابی<input aria-label="شناسه ارزیابی" value={evaluation} onChange={e=>setEvaluation(e.target.value)}/></label>
     <label>پیش‌بینی ساختگی<input aria-label="پیش‌بینی ساختگی" value={predicted} onChange={e=>setPredicted(e.target.value)}/></label>
    </div>
    <div className="market-nav">
     <button disabled={busy||!acceptable} onClick={()=>void submit('dataset')}>ثبت Dataset ساختگی</button>
     <button disabled={busy||!acceptable} onClick={()=>void submit('model')}>ثبت نسخه مدل ساختگی</button>
     <button disabled={busy||!acceptable} onClick={()=>void submit('eval')}>ارزیابی مستقل ساختگی</button>
     <button disabled={busy||!ref.test(tenant)} onClick={()=>void read()}>بازخوانی رجیستری</button>
    </div>
    <p role="status">{feedback}</p>
   </section>
   {rows&&<section className="panel" data-testid="ai-qa-inventory">
    <h2>نسخه‌های ثبت‌شده در Backend</h2>
    <p>Dataset: {rows.datasets.length} | مدل: {rows.model_versions.length} | ارزیابی: {rows.evaluations.length}</p>
    <ul>{rows.datasets.map(x=><li key={String(x.version_ref)}>Dataset: {String(x.version_ref)} — {String(x.status)}</li>)}</ul>
    <ul>{rows.model_versions.map(x=><li key={String(x.version_ref)}>مدل: {String(x.version_ref)} — {String(x.status)}</li>)}</ul>
    <ul>{rows.evaluations.map(x=><li key={String(x.evaluation_ref)}>ارزیابی: {String(x.evaluation_ref)} — MAE {String(x.mae)} / MSE {String(x.mse)}</li>)}</ul>
    <strong>مدل عملیاتی: هیچ؛ آموزش: ممنوع؛ تأیید پزشکی: انجام نشده</strong>
   </section>}
   <footer>TRN-TECH-141/142 | فقط localhost و داده ساختگی | بدون حق مدیریت Production</footer>
  </main>
 </>;
}
