import { useEffect,useMemo,useState } from 'react';

type Capability={id:string;title:string;evidenceClass:'SYNTHETIC_INTERNAL'|
 'DISCONNECTED_UI_ONLY'|'OFFLINE_POLICY_ONLY'|'NO_EVIDENCE';
 codeRef:string|null;testRef:string|null;formallyAccepted:false};
type Epic={id:string;label:string;capabilities:Capability[];missingExternalEvidence:string[]};
type Dim={id:string;label:string;status:'BLOCKED_UNVERIFIED';missingEvidence:string[]};
type Snapshot={contract:'TRN-TECH-116';status:'DRAFT_EVIDENCE_CATALOG_ONLY';
 scope:'DRAFT_UNAPPROVED';weightsApproved:false;officialAcceptanceObserved:false;
 productionReady:false;nationalReleaseAllowed:false;releaseAdmission:'DENIED_BY_DEFAULT';
 overallImplementationCompletionPct:null;formalAcceptancePct:null;
 epicCount:13;provisionalCapabilities:39;linkedNonProductionCodeTestPairs:number;
 candidatesWithoutReferences:number;externalBlockersUnverified:17;
 sourceFingerprint:string;epics:Epic[];blockedDimensions:Dim[]};
type Phase={kind:'LOADING'|'UNAVAILABLE'}|{kind:'CATALOG';snapshot:Snapshot};
const known=['SYNTHETIC_INTERNAL','DISCONNECTED_UI_ONLY','OFFLINE_POLICY_ONLY','NO_EVIDENCE'];
const dimIds=[...Array.from({length:13},(_,i)=>'TRN-EP-'+String(i+1).padStart(2,'0')),
 'MARKET-PUBLIC','MARKET-SUPPORT','MARKET-CORPORATE','NATIONAL-COVERAGE'];
const classNames:Record<string,string>={
 SYNTHETIC_INTERNAL:'کد و آزمون ساختگی',
 DISCONNECTED_UI_ONLY:'رابط جدا از سامانه واقعی',
 OFFLINE_POLICY_ONLY:'ابزار سیاست آفلاین',
 NO_EVIDENCE:'بدون ارجاع کد و آزمون'};
function valid(value:unknown):value is Snapshot{
 if(!value||typeof value!=='object')return false;
 const a=value as Partial<Snapshot>;
 if(a.contract!=='TRN-TECH-116'||a.status!=='DRAFT_EVIDENCE_CATALOG_ONLY'||
 a.scope!=='DRAFT_UNAPPROVED'||a.weightsApproved!==false||
 a.officialAcceptanceObserved!==false||a.productionReady!==false||
 a.nationalReleaseAllowed!==false||a.releaseAdmission!=='DENIED_BY_DEFAULT'||
 a.overallImplementationCompletionPct!==null||a.formalAcceptancePct!==null||
 a.epicCount!==13||a.provisionalCapabilities!==39||
 a.externalBlockersUnverified!==17||
 typeof a.sourceFingerprint!=='string'||!/^[a-f0-9]{64}$/.test(a.sourceFingerprint)||
 typeof a.linkedNonProductionCodeTestPairs!=='number'||
 typeof a.candidatesWithoutReferences!=='number'||
 !Number.isInteger(a.linkedNonProductionCodeTestPairs)||
 !Number.isInteger(a.candidatesWithoutReferences)||
 a.linkedNonProductionCodeTestPairs!+a.candidatesWithoutReferences!==39||
 !Array.isArray(a.epics)||a.epics.length!==13||
 !Array.isArray(a.blockedDimensions)||a.blockedDimensions.length!==17)
 return false;
 const seen=new Set<string>();
 const ids=new Set<string>();
 let linked=0,missing=0;
 for(const e of a.epics){
  if(!e||!dimIds.slice(0,13).includes(e.id)||seen.has(e.id)||
   typeof e.label!=='string'||!e.label||
   !Array.isArray(e.capabilities)||e.capabilities.length!==3||
   !Array.isArray(e.missingExternalEvidence)||!e.missingExternalEvidence.length)
   return false;
  seen.add(e.id);
  for(const c of e.capabilities){
   if(!c||ids.has(c.id)||!c.id.startsWith('TRN-CAP-'+e.id.slice(7)+'-')||
    !['01','02','03'].includes(c.id.slice(-2))||
    !known.includes(c.evidenceClass)||c.formallyAccepted!==false||
    typeof c.title!=='string'||!c.title)return false;
   ids.add(c.id);
   if(c.evidenceClass==='NO_EVIDENCE'){
    if(c.codeRef!==null||c.testRef!==null)return false;
    missing++;
   }else{
    if(typeof c.codeRef!=='string'||!c.codeRef||
     typeof c.testRef!=='string'||!c.testRef)return false;
    linked++;
   }
  }
 }
 if(seen.size!==13||ids.size!==39||linked!==a.linkedNonProductionCodeTestPairs||
    missing!==a.candidatesWithoutReferences)return false;
 const blockedIds=new Set<string>();
 for(const d of a.blockedDimensions){
  if(!d||!dimIds.includes(d.id)||blockedIds.has(d.id)||
   !d.label||d.status!=='BLOCKED_UNVERIFIED'||
   !Array.isArray(d.missingEvidence)||!d.missingEvidence.length)return false;
  blockedIds.add(d.id);
 }
 return blockedIds.size===17;
}
export function EpicProgressApp(){
 const [state,setState]=useState<Phase>({kind:'LOADING'});
 const [cycle,setCycle]=useState(0);
 const [term,setTerm]=useState('');
 const [kind,setKind]=useState('ALL');
 useEffect(()=>{
  const ctrl=new AbortController();
  setState({kind:'LOADING'});
  const read=async()=>{
   try{
    const res=await fetch('/internal/ops/v1/epic-progress',
     {credentials:'omit',cache:'no-store',signal:ctrl.signal});
    if(!res.ok)throw Error('UNAVAILABLE');
    const d:unknown=await res.json();
    if(!valid(d))throw Error('UNVERIFIED');
    if(!ctrl.signal.aborted)setState({kind:'CATALOG',snapshot:d});
   }catch{if(!ctrl.signal.aborted)setState({kind:'UNAVAILABLE'});}
  };
  void read();
  return ()=>ctrl.abort();
 },[cycle]);
 const data=state.kind==='CATALOG'?state.snapshot:null;
 const results=useMemo(()=>{
  if(!data)return [];
  const q=term.trim().toLocaleLowerCase();
  return data.epics.map(epic=>({
   epic,caps:epic.capabilities.filter(c=>
    (kind==='ALL'||c.evidenceClass===kind)&&
    (!q||(epic.label+' '+epic.id+' '+c.title+' '+c.id+' '+
        (c.codeRef??'')+' '+(c.testRef??'')).toLocaleLowerCase().includes(q)))
  })).filter(e=>e.caps.length);
 },[data,kind,term]);
 return <div className="epic-progress" dir="rtl">
  <a className="skip" href="#progress-main">پرش به محتوای اصلی</a>
  <header><h1>میز عملیاتی شواهد پیشرفت ترنم</h1>
   <p>نمای زنده از فهرست پیشنهادی و منبع انتشار مسدود؛ نه درصد پیشرفت و نه مجوز عرضه</p></header>
  <main id="progress-main">
   <a href="/operations">بازگشت به عملیات فنی</a>
   <section role="note" className="alert">
    <strong>تکمیل واقعی محصول قابل‌محاسبه نیست — DENIED_BY_DEFAULT</strong>
    <p>۳۹ معیار، تقسیم‌بندی پیشنهادی‌اند. وجود فایل یا CI سبز به‌معنای پذیرش رسمی نیست.</p>
   </section>
   <section className="panel">
    <div className="heading"><h2>منابع معتبر و وضعیت داده</h2>
     <button type="button" onClick={()=>setCycle(x=>x+1)}>بازخوانی شواهد</button></div>
    <p role="status" data-testid="progress-source-status">
     {state.kind==='LOADING'?'در حال خواندن منبع محلی':
      state.kind==='UNAVAILABLE'?'منبع قابل تأیید نیست؛ هیچ پیشرفتی نمایش داده نمی‌شود.':
      'فهرست فقط‌خواندنیِ پیشنهادی از منبع مخزن با وضعیت تصویب‌نشده دریافت شد.'}
    </p>
    {data&&<>
     <div className="summary" aria-label="شمارش شواهد، نه درصد پیشرفت">
      <div className="metric"><strong>{data.epicCount}</strong>حوزه اصلی</div>
      <div className="metric"><strong>{data.provisionalCapabilities}</strong>معیار پیشنهادی</div>
      <div className="metric"><strong>{data.linkedNonProductionCodeTestPairs}</strong>ارجاع فنی غیرعملیاتی</div>
      <div className="metric"><strong>{data.candidatesWithoutReferences}</strong>فاقد ارجاع</div>
      <div className="metric"><strong>{data.externalBlockersUnverified}</strong>پیش‌نیاز انتشار مسدود</div>
     </div>
     <small>شناسه اثر انگشت دو منبع: <code>{data.sourceFingerprint}</code></small>
    </>}
   </section>
   {data&&<section className="panel">
    <h2>ردیابی معیارهای پیشنهادی</h2>
    <div className="toolbar">
     <label>جست‌وجوی Epic و معیار
      <input type="search" value={term} onChange={e=>setTerm(e.target.value)}
       placeholder="TRN-CAP-01-02" autoComplete="off" /></label>
     <label>نوع شاهد فنی
      <select value={kind} onChange={e=>setKind(e.target.value)}>
       <option value="ALL">همه</option>
       {known.map(k=><option key={k} value={k}>{classNames[k]}</option>)}
      </select></label>
    </div>
    <p role="status" data-testid="progress-filter-count">
     {results.length} Epic و {results.reduce((n,e)=>n+e.caps.length,0)} معیار پیشنهادی
    </p>
    {results.map(({epic,caps})=><article key={epic.id} data-progress-epic>
     <details open={!!term||kind!=='ALL'}>
      <summary>{epic.id} — {epic.label} ({caps.length} معیار)</summary>
      <p>شواهد بیرونی احرازنشده: {epic.missingExternalEvidence.join('، ')}</p>
      <ul className="capabilities">{caps.map(c=><li key={c.id} data-progress-capability>
       <strong>{c.id} — {c.title}</strong>
       <small>{classNames[c.evidenceClass]}</small>
       <small>ارجاع کد: {c.codeRef??'موجود نیست'}</small>
       <small>ارجاع آزمون: {c.testRef??'موجود نیست'}</small>
       <small>پذیرش رسمی: NOT_VERIFIED</small>
      </li>)}</ul>
     </details>
    </article>)}
    <h2>بازارها و پوشش ملی</h2>
    <ul>{data.blockedDimensions.filter(d=>!d.id.startsWith('TRN-EP-')).map(d=>
     <li key={d.id}>{d.id} — {d.status} — {d.missingEvidence.join('، ')}</li>)}</ul>
   </section>}
   <footer>TRN-TECH-116 | DRAFT_UNAPPROVED | فقط خواندن محلی | هیچ تأیید واقعی یا دسترسی عضو ایجاد نشده</footer>
  </main>
 </div>;
}
