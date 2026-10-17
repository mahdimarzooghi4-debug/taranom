import {useEffect,useMemo,useState} from 'react';

type Requirement={
 requirement:string;blockedDomainIds:string[];affectedEpicIds:string[];
 affectedMarketOrNationalIds:string[];policyRefs:string[];
 linkedNonProductionCandidatePairsInAffectedEpics:number;
 decisionOwner:'NOT_ASSIGNED';evidenceStatus:'BLOCKED_UNVERIFIED';
 humanDecisionStatus:'NOT_OBSERVED';formalAcceptanceObserved:false;
};
type EngineeringCandidate={
 id:string;epicId:string;title:string;
 evidenceClass:'SYNTHETIC_INTERNAL'|'DISCONNECTED_UI_ONLY'|'OFFLINE_POLICY_ONLY'|'NO_EVIDENCE';
 codeRef:string|null;testRef:string|null;
 evidenceLane:'MISSING_CODE_TEST_REFERENCE'|'NONPRODUCTION_REFERENCE_ONLY';
 epicMissingExternalEvidence:string[];relatedUnverifiedRequirements:string[];
 formalAcceptanceObserved:false;engineeringReadiness:'NOT_ESTABLISHED';
};
type Source={
 contract:'TRN-TECH-165';status:'DRAFT_UNVERIFIED_DEPENDENCY_WORKLIST';
 releaseAdmission:'DENIED_BY_DEFAULT';formalApprovalObserved:false;
 productionReady:false;nationalReleaseAllowed:false;
 blockedDomains:17;candidateCapabilities:39;requiredOwnersUnassigned:18;
 sourceFingerprint:string;workItems:Requirement[];
 engineeringCandidates:EngineeringCandidate[];
 technicalReferencesPresent:26;technicalReferencesMissing:13;
};
type State={kind:'LOADING'|'UNAVAILABLE'}|{kind:'READY';snapshot:Source};
const domains=[...Array.from({length:13},(_,i)=>'TRN-EP-'+String(i+1).padStart(2,'0')),
 'MARKET-PUBLIC','MARKET-SUPPORT','MARKET-CORPORATE','NATIONAL-COVERAGE'];
const required=[
 'ALL_3_MARKET_OPERATIONS','AUTHENTIC_MEMBERSHIP_AND_ACTIVATION',
 'CLINICAL_SAFETY_AND_RESPONSIBILITY','COMPLAINTS_LEGAL_AUTHORITY_AND_PROCESS',
 'CONSENTED_AI_DATASET_AND_INDEPENDENT_EVALUATION','DATA_RIGHTS_AND_CONSENT',
 'EMERGENCY_PROTOCOL_AND_CAPACITY','FINANCIAL_LICENSE_BACKING_AND_AUDIT',
 'HUMAN_AI_COMMITTEE_AND_RELEASE_APPROVAL','INDEPENDENT_REVIEWS_AND_RELEASE_APPROVAL',
 'INSURER_AGREEMENT_AND_ENTITLEMENT','LEGAL_OPERATOR_IDENTITY',
 'NATIONAL_COVERAGE_EVIDENCE','OFFICIAL_BILLING_AND_MEMBER_ACKNOWLEDGMENT',
 'PRODUCTION_HOSTING_AND_SECURITY','PROVIDER_LICENSE_AND_CONTRACT',
 'REAL_EXTERNAL_INTEGRATIONS','VERIFIED_MEMBER_IDENTITY'];
function valid(x:unknown):x is Source{
 if(!x||typeof x!=='object')return false;
 const a=x as Partial<Source>;
 if(a.contract!=='TRN-TECH-165'||a.status!=='DRAFT_UNVERIFIED_DEPENDENCY_WORKLIST'||
 a.releaseAdmission!=='DENIED_BY_DEFAULT'||a.formalApprovalObserved!==false||
 a.productionReady!==false||a.nationalReleaseAllowed!==false||
 a.blockedDomains!==17||a.candidateCapabilities!==39||
 a.requiredOwnersUnassigned!==18||
 typeof a.sourceFingerprint!=='string'||!/^[a-f0-9]{64}$/.test(a.sourceFingerprint)||
 !Array.isArray(a.workItems)||a.workItems.length!==18||
 a.technicalReferencesPresent!==26||a.technicalReferencesMissing!==13||
 !Array.isArray(a.engineeringCandidates)||a.engineeringCandidates.length!==39)
 return false;
 const seen=new Set<string>(),covered=new Set<string>();
 for(const item of a.workItems){
  if(!item||!required.includes(item.requirement)||seen.has(item.requirement)||
   item.decisionOwner!=='NOT_ASSIGNED'||item.evidenceStatus!=='BLOCKED_UNVERIFIED'||
   item.humanDecisionStatus!=='NOT_OBSERVED'||item.formalAcceptanceObserved!==false||
   !Array.isArray(item.blockedDomainIds)||!item.blockedDomainIds.length||
   !Array.isArray(item.affectedEpicIds)||!Array.isArray(item.affectedMarketOrNationalIds)||
   !Array.isArray(item.policyRefs)||!item.policyRefs.length||
   !Number.isInteger(item.linkedNonProductionCandidatePairsInAffectedEpics)||
   item.linkedNonProductionCandidatePairsInAffectedEpics<0||
   item.linkedNonProductionCandidatePairsInAffectedEpics>39||
   item.policyRefs.some(p=>typeof p!=='string'||!/^TRN-BIZ-[0-9]{3}$/.test(p))||
   new Set(item.policyRefs).size!==item.policyRefs.length)return false;
  seen.add(item.requirement);
  const ids=new Set<string>();
  for(const id of item.blockedDomainIds){
   if(typeof id!=='string'||!domains.includes(id)||ids.has(id))return false;
   ids.add(id);covered.add(id);
  }
  if(item.affectedEpicIds.length+item.affectedMarketOrNationalIds.length!==ids.size||
   item.affectedEpicIds.some(v=>!ids.has(v)||!v.startsWith('TRN-EP-'))||
   item.affectedMarketOrNationalIds.some(v=>!ids.has(v)||v.startsWith('TRN-EP-')))
   return false;
 }
 if(seen.size!==18||covered.size!==17)return false;
 const candidates=new Set<string>();const epicCounts=new Map<string,number>();
 let nonProduction=0,missing=0;
 const evidenceClasses=['SYNTHETIC_INTERNAL','DISCONNECTED_UI_ONLY',
  'OFFLINE_POLICY_ONLY','NO_EVIDENCE'];
 for(const c of a.engineeringCandidates){
  if(!c||typeof c.id!=='string'||typeof c.epicId!=='string'||
   !domains.slice(0,13).includes(c.epicId)||
   !c.id.startsWith('TRN-CAP-'+c.epicId.slice(7)+'-')||
   c.id.length!==13||!['01','02','03'].includes(c.id.slice(-2))||
   candidates.has(c.id)||typeof c.title!=='string'||!c.title.trim()||
   !evidenceClasses.includes(c.evidenceClass)||
   c.formalAcceptanceObserved!==false||c.engineeringReadiness!=='NOT_ESTABLISHED'||
   !Array.isArray(c.epicMissingExternalEvidence)||
   c.epicMissingExternalEvidence.length===0||
   c.epicMissingExternalEvidence.some(x=>typeof x!=='string'||!required.includes(x))||
   !Array.isArray(c.relatedUnverifiedRequirements)||!c.relatedUnverifiedRequirements.length||
   c.relatedUnverifiedRequirements.some(x=>!seen.has(x)))return false;
  const knownRelated=a.workItems.filter(w=>w.affectedEpicIds.includes(c.epicId))
   .map(w=>w.requirement);
  if(knownRelated.length!==c.relatedUnverifiedRequirements.length||
    new Set(c.relatedUnverifiedRequirements).size!==knownRelated.length||
    c.relatedUnverifiedRequirements.some(x=>!knownRelated.includes(x)))return false;
  if(c.evidenceClass==='NO_EVIDENCE'){
   if(c.evidenceLane!=='MISSING_CODE_TEST_REFERENCE'||c.codeRef!==null||c.testRef!==null)return false;
   missing++;
  }else{
   if(c.evidenceLane!=='NONPRODUCTION_REFERENCE_ONLY'||
    typeof c.codeRef!=='string'||!c.codeRef||
    typeof c.testRef!=='string'||!c.testRef)return false;
   nonProduction++;
  }
  candidates.add(c.id);
  epicCounts.set(c.epicId,(epicCounts.get(c.epicId)??0)+1);
 }
 // TRN-TECH-168: a self-consistent but forged join must never look authoritative.
 // Reconstruct each Epic's missing evidence and each row's nonproduction pair count
 // from the independently projected cross-references, not from claimed counts.
 const presentByEpic=new Map<string,number>();
 for(const c of a.engineeringCandidates){
  if(c.evidenceLane==='NONPRODUCTION_REFERENCE_ONLY')
   presentByEpic.set(c.epicId,(presentByEpic.get(c.epicId)??0)+1);
  const expected=a.workItems.filter(w=>w.affectedEpicIds.includes(c.epicId))
   .map(w=>w.requirement);
  if(c.epicMissingExternalEvidence.length!==expected.length||
   new Set(c.epicMissingExternalEvidence).size!==expected.length||
   c.epicMissingExternalEvidence.some(x=>!expected.includes(x)))return false;
 }
 for(const w of a.workItems){
  const derived=w.affectedEpicIds.reduce((sum,id)=>sum+(presentByEpic.get(id)??0),0);
  if(w.linkedNonProductionCandidatePairsInAffectedEpics!==derived)return false;
 }
 return candidates.size===39&&epicCounts.size===13&&
  [...epicCounts.values()].every(n=>n===3)&&nonProduction===26&&missing===13;
}
export function ReleaseDependenciesApp(){
 const [state,setState]=useState<State>({kind:'LOADING'});
 const [cycle,setCycle]=useState(0);
 const [query,setQuery]=useState('');
 const [domain,setDomain]=useState('ALL');
 const [engineeringEpic,setEngineeringEpic]=useState('ALL');
 const [lane,setLane]=useState('ALL');
 const [selectedRequirement,setSelectedRequirement]=useState('ALL');
 useEffect(()=>{
  const abort=new AbortController();setState({kind:'LOADING'});
  const read=async()=>{
   try{
    const response=await fetch('/internal/ops/v1/dependencies',
     {method:'GET',credentials:'omit',cache:'no-store',signal:abort.signal});
    if(!response.ok)throw Error('SOURCE_UNAVAILABLE');
    const result:unknown=await response.json();
    if(!valid(result))throw Error('SOURCE_UNTRUSTED');
    if(!abort.signal.aborted)setState({kind:'READY',snapshot:result});
   }catch{if(!abort.signal.aborted)setState({kind:'UNAVAILABLE'});}
  };
  void read();return ()=>abort.abort();
 },[cycle]);
 const source=state.kind==='READY'?state.snapshot:null;
 const shown=useMemo(()=>source?.workItems.filter(x=>
  (domain==='ALL'||x.blockedDomainIds.includes(domain))&&
  (!query.trim()||(x.requirement+' '+x.blockedDomainIds.join(' ')+
   ' '+x.policyRefs.join(' ')).toLowerCase().includes(query.trim().toLowerCase()))
 )??[],[source,domain,query]);
 const engineering=useMemo(()=>source?.engineeringCandidates.filter(x=>
  (engineeringEpic==='ALL'||x.epicId===engineeringEpic)&&
  (lane==='ALL'||x.evidenceLane===lane)&&
  (selectedRequirement==='ALL'||x.relatedUnverifiedRequirements.includes(selectedRequirement))
 )??[],[source,engineeringEpic,lane,selectedRequirement]);
 return <div dir="rtl" className="release-dependencies">
  <a href="#dependencies-main" className="skip">پرش به محتوای اصلی</a>
  <header><h1>صف وابستگی‌های تکمیل محصول ترنم</h1>
   <p>خواندن زنده از سرویس محلی .NET؛ تصمیم‌ها و مدارک واقعی هنوز احراز نشده‌اند.</p></header>
  <main id="dependencies-main">
   <nav aria-label="عملیات"><a href="/operations">عملیات فنی</a> | <a href="/operations/progress">قابلیت‌های پیشنهادی</a></nav>
   <section className="alert" role="note">
    <strong>عرضه جامع سراسری: DENIED_BY_DEFAULT</strong>
    <p>فهرست کار و نیاز به شواهد است، نه تعیین متولی، اولویت، تأیید، درصد پیشرفت یا مجوز استقرار.</p>
   </section>
   <section className="panel">
    <div className="heading"><h2>وضعیت منابع تصمیم</h2>
     <button type="button" onClick={()=>setCycle(n=>n+1)}>بازخوانی وابستگی‌ها</button></div>
    <p role="status" data-testid="dependencies-status">
     {state.kind==='LOADING'?'در حال دریافت فهرست مستند از سرویس محلی':
      state.kind==='UNAVAILABLE'?'منبع یا اتصال قابل تأیید نیست؛ هیچ وابستگی تأیید نمی‌شود.':
      'فهرست وابستگی‌های باز از منبع موجود خوانده شد؛ هیچ موردی تأیید نشده است.'}</p>
    {source&&<>
     <div className="summary" aria-label="تعداد موضوعات باز، نه پیشرفت">
      <div className="metric"><strong>{source.blockedDomains}</strong>حوزه انتشار مسدود</div>
      <div className="metric"><strong>{source.workItems.length}</strong>دسته وابستگی باز</div>
      <div className="metric"><strong>{source.candidateCapabilities}</strong>قابلیت پیشنهادی، نه پذیرفته‌شده</div>
      <div className="metric"><strong>{source.requiredOwnersUnassigned}</strong>دسته وابستگی بدون متولی ثبت‌شده</div>
      <div className="metric"><strong>{source.technicalReferencesPresent}</strong>معیار با ارجاع فنی آزمایشی</div>
      <div className="metric"><strong>{source.technicalReferencesMissing}</strong>معیار فاقد ارجاع کد و آزمون</div>
     </div>
     <small>اثر انگشت منبع: <code>{source.sourceFingerprint}</code></small>
    </>}
   </section>
   {source&&<section className="panel">
    <h2>بررسی وابستگی‌ها و منابع Business</h2>
    <div className="toolbar">
     <label>جست‌وجوی مدرک، حوزه یا تصمیم
      <input type="search" value={query} autoComplete="off"
       onChange={e=>setQuery(e.target.value)} placeholder="TRN-BIZ-035"/></label>
     <label>حوزه متأثر
      <select value={domain} onChange={e=>setDomain(e.target.value)}>
       <option value="ALL">تمام حوزه‌ها</option>
       {domains.map(x=><option key={x} value={x}>{x}</option>)}
      </select></label>
    </div>
    <p role="status" data-testid="dependencies-filter-count">{shown.length} دسته وابستگی باز</p>
    <div data-testid="dependencies-list">{shown.map(item=><article data-dependency key={item.requirement}>
     <h3>{item.requirement}</h3>
     <p>حوزه‌های متأثر: {item.blockedDomainIds.join('، ')}</p>
     <p>مستندات تصمیم: {item.policyRefs.join('، ')}</p>
     <p>ارجاع‌های کد و آزمون غیرعملیاتی در Epicهای متأثر: {item.linkedNonProductionCandidatePairsInAffectedEpics}</p>
     <p><strong>مدرک: {item.evidenceStatus} | تصمیم انسانی: {item.humanDecisionStatus} | مسئول: {item.decisionOwner}</strong></p>
     <button type="button" onClick={()=>{setEngineeringEpic('ALL');setLane('ALL');setSelectedRequirement(item.requirement);}}>نمایش قابلیت‌های مرتبط</button>
    </article>)}</div>
   </section>}
   {source&&<section className="panel" aria-labelledby="engineering-title">
    <h2 id="engineering-title">ماتریس مهندسی و وابستگی بیرونی هر Epic</h2>
    <p>ارجاع کد و آزمون تنها نشانه وجود نمونه فنی است، نه مجوز کدنویسی قابلیت واقعی،
     تکمیل آن، تأیید مدرک یا آمادگی Production. همه موارد به شواهد واقعی وابسته‌اند.</p>
    <div className="toolbar">
     <label>حوزه Epic
      <select value={engineeringEpic} onChange={e=>setEngineeringEpic(e.target.value)}>
       <option value="ALL">تمام ۱۳ Epic</option>
       {domains.slice(0,13).map(id=><option key={id} value={id}>{id}</option>)}
      </select></label>
     <label>وابستگی بیرونی برای ردیابی قابلیت‌ها
      <select value={selectedRequirement} onChange={e=>setSelectedRequirement(e.target.value)}>
       <option value="ALL">تمام وابستگی‌ها</option>
       {source.workItems.map(w=><option key={w.requirement} value={w.requirement}>{w.requirement}</option>)}
      </select></label>
     <label>نوع فاصله فنی
      <select value={lane} onChange={e=>setLane(e.target.value)}>
       <option value="ALL">تمام ۳۹ معیار پیشنهادی</option>
       <option value="NONPRODUCTION_REFERENCE_ONLY">دارای ارجاع کد و آزمون غیرعملیاتی</option>
       <option value="MISSING_CODE_TEST_REFERENCE">فاقد ارجاع کد و آزمون</option>
      </select></label>
    </div>
    <p role="status" data-testid="engineering-count">
     {engineering.length} معیار پیشنهادی مرتبط؛ آمادگی مهندسی همه نامشخص و انتشار مسدود است.
    </p>
    {engineering.map(c=><article key={c.id} data-engineering-candidate>
     <h3>{c.id} — {c.title}</h3>
     <p>حوزه: {c.epicId} | مسیر مدرک: {c.evidenceLane}</p>
     <p>ارجاع کد: <code>{c.codeRef??'فاقد ارجاع'}</code></p>
     <p>ارجاع آزمون: <code>{c.testRef??'فاقد ارجاع'}</code></p>
     <p>وابستگی‌های بیرونی Epic: {c.epicMissingExternalEvidence.join('، ')}</p>
     <p>دسته‌های تصمیم باز مرتبط: {c.relatedUnverifiedRequirements.join('، ')}</p>
     <strong>آمادگی مهندسی: {c.engineeringReadiness} | پذیرش رسمی: NOT_OBSERVED</strong>
    </article>)}
   </section>}
   <footer>TRN-TECH-165/166 | فقط‌خواندنی | فاقد اختیار انسانی/بالینی/مالی | DENIED_BY_DEFAULT</footer>
  </main>
 </div>;
}
