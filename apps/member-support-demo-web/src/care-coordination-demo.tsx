import {useState} from 'react';
type Action='REQUEST_NONCLINICAL_COORDINATION'|
 'RECORD_UNVERIFIED_COORDINATOR_CONTACT'|'ATTACH_UNVERIFIED_CLINICAL_SOURCE';
type Command={requestId:string;expectedRevision:number;action:Action;
 memberRef:string;originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:string|null;unverifiedNoteOrDocumentRef:string|null};
type Event={revision:number;action:string;requestId:string;memberRef:string;
 originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:string|null;unverifiedNoteOrDocumentRef:string|null};
type Snapshot={contract:'TRN-TECH-115';syntheticOnly:true;unverifiedClaimsOnly:true;
 caseRef:string;outcome:string;revision:number;events:Event[];
 realMemberAuthenticated:false;realCoordinatorVerified:false;
 clinicalSourceVerified:false;clinicianAuthorized:false;referralOrdered:false;
 referralDelivered:false;appointmentBooked:false;treatmentStarted:false;
 medicalRecordAccessGranted:false;emergencyServiceDispatched:false;
 consentGranted:false;walletMutated:false;nationalReleaseAllowed:false};
const newRef=()=> 'test:care-'+crypto.randomUUID().replaceAll('-','');
const zero='00000000-0000-0000-0000-000000000000';
const denialKeys=['realMemberAuthenticated','realCoordinatorVerified',
 'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
 'referralDelivered','appointmentBooked','treatmentStarted',
 'medicalRecordAccessGranted','emergencyServiceDispatched','consentGranted',
 'walletMutated','nationalReleaseAllowed'] as const;
// The client must recheck exact event lineage before displaying a source
// snapshot, suggesting next steps or accepting a successful command ACK.
// This validation is not authorization or a replacement for the PG digest/audit.
const testRef=(v:unknown):v is string=>typeof v==='string'&&v.length<=100&&
 /^test:[A-Za-z0-9_:-]+$/.test(v);
const uuid=(v:unknown):v is string=>typeof v==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);
function valid(x:unknown,caseRef:string):x is Snapshot{
 if(!x||typeof x!=='object'||Array.isArray(x)||!testRef(caseRef))return false;
 const s=x as Partial<Snapshot>;
 if(s.contract!=='TRN-TECH-115'||s.syntheticOnly!==true||
    s.unverifiedClaimsOnly!==true||s.caseRef!==caseRef||
    !['READ','RecordedUnverified','Replayed'].includes(String(s.outcome))||
    !Number.isSafeInteger(s.revision)||s.revision!<0||
    !Array.isArray(s.events)||s.events.length!==s.revision||
    !denialKeys.every(k=>s[k]===false))return false;
 let root:Event|undefined;
 const requestIds=new Set<string>();
 for(const [i,event] of s.events.entries()){
  if(!event||event.revision!==i+1||!uuid(event.requestId)||
     requestIds.has(event.requestId.toLowerCase())||
     !testRef(event.memberRef)||!testRef(event.originalMemberRequestRef))
    return false;
  requestIds.add(event.requestId.toLowerCase());
  if(i===0){
   if(event.action!=='RequestNonclinicalCoordination'||
      event.originalSubmissionRequestId!==zero||
      event.claimedActorRef!==null||
      event.unverifiedNoteOrDocumentRef!==null)return false;
   root=event;
  }else if(!root||
      !['RecordUnverifiedCoordinatorContact',
        'AttachUnverifiedClinicalSourceReference'].includes(event.action)||
      event.memberRef!==root.memberRef||
      event.originalMemberRequestRef!==root.originalMemberRequestRef||
      event.originalSubmissionRequestId!==root.requestId||
      !testRef(event.claimedActorRef)||
      !testRef(event.unverifiedNoteOrDocumentRef))return false;
 }
 return true;
}
function acknowledged(s:Snapshot,c:Command,viaRead=false):boolean{
 const actions:Record<Action,string>={
  REQUEST_NONCLINICAL_COORDINATION:'RequestNonclinicalCoordination',
  RECORD_UNVERIFIED_COORDINATOR_CONTACT:'RecordUnverifiedCoordinatorContact',
  ATTACH_UNVERIFIED_CLINICAL_SOURCE:'AttachUnverifiedClinicalSourceReference'};
 const match=s.events.filter(e=>e.requestId===c.requestId);
 if(match.length!==1)return false;
 const e=match[0],rev=c.expectedRevision+1;
 return e.revision===rev&&e.action===actions[c.action]&&
  e.memberRef===c.memberRef&&
  e.originalMemberRequestRef===c.originalMemberRequestRef&&
  e.originalSubmissionRequestId===c.originalSubmissionRequestId&&
  e.claimedActorRef===c.claimedActorRef&&
  e.unverifiedNoteOrDocumentRef===c.unverifiedNoteOrDocumentRef&&
  (s.outcome==='Replayed'||(viaRead&&s.outcome==='READ')||
   (s.outcome==='RecordedUnverified'&&s.revision===rev));
}
type SuggestedStep={
 action:Action|null;label:string;contactClaims:number;sourceClaims:number
};
// A navigational guide over source-backed, unverified *claims*. The two
// follow-up types have no clinical prerequisite order; they are suggestions,
// not authorization, clinical outcomes, SLAs, or business approvals.
function nextClaimStep(s:Snapshot|null,caseRef:string):SuggestedStep{
 if(!s||s.caseRef!==caseRef)
  return {action:null,label:'ابتدا پرونده را از PostgreSQL بخوانید.',
    contactClaims:0,sourceClaims:0};
 const contacts=s.events.filter(e=>e.action==='RecordUnverifiedCoordinatorContact').length;
 const sources=s.events.filter(e=>e.action==='AttachUnverifiedClinicalSourceReference').length;
 if(s.revision===0)return {action:'REQUEST_NONCLINICAL_COORDINATION',
    label:'گام ۱: ثبت درخواست هماهنگی صرفاً ادعایی',contactClaims:0,sourceClaims:0};
 if(contacts===0)return {action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
    label:'گام پیشنهادی: ثبت ادعای تماس هماهنگ‌کننده',contactClaims:contacts,sourceClaims:sources};
 if(sources===0)return {action:'ATTACH_UNVERIFIED_CLINICAL_SOURCE',
    label:'گام پیشنهادی: پیوست ارجاع منبع تأییدنشده',contactClaims:contacts,sourceClaims:sources};
 return {action:null,
   label:'هر دو نوع ادعای پیگیری ثبت شده‌اند؛ هیچ خدمت یا تماس واقعی احراز نشده است.',
   contactClaims:contacts,sourceClaims:sources};
}
export function CareCoordinationDemo(){
 const [caseRef,setCaseRef]=useState(newRef);
 const [memberRef,setMemberRef]=useState('test:member-claim');
 const [requestRef,setRequestRef]=useState('test:coordination-reason');
 const [coordinator,setCoordinator]=useState('test:claimed-coordinator');
 const [contactNote,setContactNote]=useState('test:claimed-contact-note');
 const [clinicalSource,setClinicalSource]=useState('test:opaque-clinical-source');
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);
 const [uncertain,setUncertain]=useState<{ref:string;body:string}|null>(null);
 const [busy,setBusy]=useState(false);
 const [status,setStatus]=useState('فقط شناسه‌های ساختگی؛ هیچ درخواست واقعی ارسال نشده است.');
 function change(fn:(s:string)=>void,value:string){
  fn(value);setSnapshot(null);setStatus('ورودی تغییر کرد؛ تاریخچه باید مجدداً از منبع خوانده شود.');
 }
 async function read(){
  if(busy||uncertain)return;setBusy(true);setSnapshot(null);
  try{
   const response=await fetch('/internal/demo/v1/care-coordination/'+encodeURIComponent(caseRef),
    {credentials:'omit',cache:'no-store'});
   if(!response.ok)throw Error('HTTP_DENIED');
   const data:unknown=await response.json();
   if(!valid(data,caseRef))throw Error('INVALID_SOURCE');
   setSnapshot(data);setStatus('تاریخچه ممیزی‌شده از PostgreSQL آزمایشی خوانده شد.');
  }catch{setStatus('منبع قابل تأیید نیست؛ هیچ وضعیت موفقی نمایش داده نمی‌شود.');}
  finally{setBusy(false);}
 }
 async function send(original:{ref:string;body:string}){
  if(busy)return;setBusy(true);setSnapshot(null);
  try{
   const result=await fetch('/internal/demo/v1/care-coordination/'+
     encodeURIComponent(original.ref)+'/commands',{
      method:'POST',credentials:'omit',cache:'no-store',
      headers:{'Content-Type':'application/json'},body:original.body
     });
   if(!result.ok)throw Error('DENIED_OR_UNCERTAIN');
   const x:unknown=await result.json();
   if(!valid(x,original.ref)||!['RecordedUnverified','Replayed'].includes(x.outcome))
      throw Error('INVALID_ACK');
   const cmd=JSON.parse(original.body) as Command;
   if(!acknowledged(x,cmd))throw Error('UNBOUND_ACK');
   // A forged 200 response can describe a valid-looking event that has
   // never been committed. Do not release the UUID latch until the exact
   // event is seen in a separate independently validated PostgreSQL GET.
   const check=await fetch('/internal/demo/v1/care-coordination/'+
     encodeURIComponent(original.ref),{credentials:'omit',cache:'no-store'});
   if(!check.ok)throw Error('SOURCE_READ_DENIED');
   const persisted:unknown=await check.json();
   if(!valid(persisted,original.ref)||persisted.outcome!=='READ'||
      !acknowledged(persisted,cmd,true))throw Error('UNBOUND_SOURCE');
   setSnapshot(persisted);setUncertain(null);setStatus(x.outcome==='Replayed'?
    'همان درخواست، بدون رویداد اضافی بازپخش شد.':
    'رویداد ادعایی ثبت شد؛ ارجاع، تماس واقعی یا نوبت تأیید نشده است.');
  }catch{
   setUncertain(original);setStatus('نتیجه ارسال نامشخص است؛ فقط بازپخش دستی دقیق مجاز است.');
  }finally{setBusy(false);}
 }
 async function inspectUncertain(){
  if(busy||!uncertain)return;
  const original=uncertain;
  setBusy(true);setSnapshot(null);
  try{
   const res=await fetch('/internal/demo/v1/care-coordination/'+
     encodeURIComponent(original.ref),{credentials:'omit',cache:'no-store'});
   if(!res.ok)throw Error('READ_DENIED');
   const x:unknown=await res.json();
   const c=JSON.parse(original.body) as Command;
   if(!valid(x,original.ref)||x.outcome!=='READ'||!acknowledged(x,c,true))
    throw Error('UNBOUND_JOURNAL_READ');
   setSnapshot(x);setUncertain(null);
   setStatus('همان فرمان در ژورنال آزمایشی پیدا شد؛ بدون بازپخش یا ثبت جدید بازیابی شد.');
  }catch{
   setStatus('ثبت همان فرمان با خواندن تأیید نشد؛ قفل و بازپخش دستی حفظ شد.');
  }finally{setBusy(false);}
 }
 function canIssue(action:Action|null):boolean{
  if(!snapshot||!action||snapshot.caseRef!==caseRef||busy||uncertain)return false;
  if(action==='REQUEST_NONCLINICAL_COORDINATION')
   return snapshot.revision===0&&testRef(memberRef)&&testRef(requestRef);
  const value=action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'?contactNote:clinicalSource;
  return snapshot.revision>0&&testRef(coordinator)&&testRef(value);
 }
 function issue(action:Action){
  if(!canIssue(action)||!snapshot)return;
  const origin=snapshot.events[0];
  if(action!=='REQUEST_NONCLINICAL_COORDINATION'&&!origin)return;
  const first=action==='REQUEST_NONCLINICAL_COORDINATION';
  const cmd:Command={requestId:crypto.randomUUID(),
   expectedRevision:snapshot.revision,action,
   memberRef:first?memberRef:origin.memberRef,
   originalMemberRequestRef:first?requestRef:origin.originalMemberRequestRef,
   originalSubmissionRequestId:first?zero:origin.requestId,
   claimedActorRef:first?null:coordinator,
   unverifiedNoteOrDocumentRef:first?null:
    action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'?contactNote:clinicalSource};
  void send({ref:caseRef,body:JSON.stringify(cmd)});
 }
 const suggestion=nextClaimStep(snapshot,caseRef);
 const guideAction=uncertain?null:suggestion.action;
 const eventLabel=(action:string)=>action==='RecordUnverifiedCoordinatorContact'
  ?'ادعای تماس تأییدنشده':action==='AttachUnverifiedClinicalSourceReference'
  ?'مرجع بالینی ادعایی تأییدنشده':'درخواست هماهنگی غیرتشخیصی';
 return <section className="care-demo" dir="rtl">
  <h1>هماهنگی غیرتشخیصی و پیگیری ادعایی عضو</h1>
  <p className="risk">فقط محیط آزمایشی: بدون احراز هویت، تماس واقعی، تعیین تیم درمان،
  تشخیص، ارجاع پزشکی، نوبت‌دهی یا انتقال پرونده. برای وضعیت اضطراری باید از
  خدمات اورژانس محلی استفاده شود؛ این فرم خدمات فوریت ندارد.</p>
  <fieldset><legend>درخواست هماهنگی غیرتشخیصی</legend>
   <label>شناسه آزمایشی پرونده
    <input dir="ltr" value={caseRef} disabled={busy||!!uncertain}
      onChange={e=>change(setCaseRef,e.target.value)}/></label>
   <label>شناسه آزمایشی عضو
    <input dir="ltr" value={memberRef} disabled={busy||!!uncertain}
      onChange={e=>change(setMemberRef,e.target.value)}/></label>
   <label>ارجاع مبهم درخواست
    <input dir="ltr" value={requestRef} disabled={busy||!!uncertain}
      onChange={e=>change(setRequestRef,e.target.value)}/></label>
   <button disabled={busy||!!uncertain} onClick={()=>void read()}>
    خواندن پرونده هماهنگی</button>
   <button disabled={!canIssue('REQUEST_NONCLINICAL_COORDINATION')}
    onClick={()=>issue('REQUEST_NONCLINICAL_COORDINATION')}>
    ثبت درخواست ادعایی هماهنگی</button>
  </fieldset>
  <fieldset><legend>پیگیری‌های تأییدنشده، جدا از تصمیم پزشکی</legend>
   <label>شناسه مدعی هماهنگ‌کننده
    <input dir="ltr" value={coordinator} disabled={busy||!!uncertain}
      onChange={e=>change(setCoordinator,e.target.value)}/></label>
   <label>مرجع یادداشت تماس ادعایی، تأییدنشده
    <input dir="ltr" value={contactNote} disabled={busy||!!uncertain}
      onChange={e=>change(setContactNote,e.target.value)}/></label>
   <button disabled={!canIssue('RECORD_UNVERIFIED_COORDINATOR_CONTACT')}
    onClick={()=>issue('RECORD_UNVERIFIED_COORDINATOR_CONTACT')}>
    ثبت ادعای تماس هماهنگ‌کننده</button>
   <label>مرجع منبع بالینی ادعایی، تأییدنشده
    <input dir="ltr" value={clinicalSource} disabled={busy||!!uncertain}
      onChange={e=>change(setClinicalSource,e.target.value)}/></label>
   <button disabled={!canIssue('ATTACH_UNVERIFIED_CLINICAL_SOURCE')}
    onClick={()=>issue('ATTACH_UNVERIFIED_CLINICAL_SOURCE')}>
    پیوست ارجاع به منبع بالینیِ تأییدنشده</button>
   <p>این دو ورودی صرفاً ادعاهای مجزا هستند؛ هیچ سند، اجازه درمان یا رضایت واقعی ایجاد نمی‌شود.</p>
  </fieldset>
  <section role="region" aria-label="راهنمای پیگیری غیرتشخیصی آزمایشی"
    data-testid="care-guided-workflow">
   <h2>راهنمای پیگیری ادعایی عضو — بدون تصمیم بالینی</h2>
   <p>گام پیشنهادی تنها از رویدادهای خوانده‌شده PostgreSQL به دست می‌آید.
      ادعای تماس و ارجاع منبع الزاماً ترتیب حقوقی یا درمانی ندارند؛
      هیچ فرمانی به‌صورت خودکار اجرا نمی‌شود.</p>
   <p data-testid="care-guided-step" aria-live="polite">
    {busy?'در حال بررسی؛ فرمان تازه مجاز نیست.':
     uncertain?'پاسخ فرمان قبلی نامعلوم است؛ قبل از هر گام جدید همان درخواست را بازیابی کنید.':
     suggestion.label}
   </p>
   {snapshot&&<p data-testid="care-guided-counts">
     نسخه ژورنال: {snapshot.revision} |
     ادعاهای تماس: {suggestion.contactClaims} |
     ارجاعات تأییدنشده: {suggestion.sourceClaims}
   </p>}
   {guideAction&&<button type="button"
     disabled={!canIssue(guideAction)}
     onClick={()=>issue(guideAction)}>ثبت گام پیشنهادی با کلیک مستقل</button>}
   <p>این راهنما مسئولیت پزشکی، تماس واقعی، ارجاع، درمان، نوبت،
      رضایت یا دسترسی به پرونده ایجاد نمی‌کند. در موارد اضطراری
      از مسیر خدمات اورژانس رسمی استفاده شود.</p>
  </section>
  {uncertain&&<section role="alert"><p>ممکن است رویداد در سرور ثبت شده باشد.
   فرمان جدید قفل است؛ همان متن اصلی و UUID فقط با کلیک جداگانه تکرار می‌شود.</p>
   <button disabled={busy} onClick={()=>void inspectUncertain()}>
    بررسی فقط‌خواندنی فرمان قبلی</button>
   <button disabled={busy} onClick={()=>void send(uncertain)}>
    بازپخش دستی دقیق فرمان قبلی</button></section>}
  <p role="status" data-testid="care-status">{status}</p>
  {snapshot&&<section className="timeline" role="region" aria-label="تاریخچه هماهنگی ادعایی">
   <p>نسخه پرونده: <strong>{snapshot.revision}</strong></p>
   <p>ارجاع پزشکی، تماس واقعی، نوبت و دسترسی به پرونده: <strong>هیچ‌کدام</strong></p>
   <ol>{snapshot.events.map(e=><li key={e.revision}>
    نسخه {e.revision}: {eventLabel(e.action)} ({e.action}) — {e.unverifiedNoteOrDocumentRef??'بدون سند'}
   </li>)}</ol>
  </section>}
 </section>;
}
