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
function valid(x:unknown,caseRef:string):x is Snapshot{
 if(!x||typeof x!=='object')return false;
 const s=x as Partial<Snapshot>;
 if(s.contract!=='TRN-TECH-115'||s.syntheticOnly!==true||
    s.unverifiedClaimsOnly!==true||s.caseRef!==caseRef||
    !['READ','RecordedUnverified','Replayed'].includes(String(s.outcome))||
    !Number.isSafeInteger(s.revision)||s.revision!<0||
    !Array.isArray(s.events)||s.events.length!==s.revision||
    !s.events.every((e,i)=>e.revision===i+1&&
      ['RequestNonclinicalCoordination','RecordUnverifiedCoordinatorContact',
       'AttachUnverifiedClinicalSourceReference'].includes(e.action)))return false;
 return denialKeys.every(k=>s[k]===false);
}
export function CareCoordinationDemo(){
 const [caseRef,setCaseRef]=useState(newRef);
 const [memberRef,setMemberRef]=useState('test:member-claim');
 const [requestRef,setRequestRef]=useState('test:coordination-reason');
 const [coordinator,setCoordinator]=useState('test:claimed-coordinator');
 const [source,setSource]=useState('test:opaque-clinical-source');
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
   if(x.revision<cmd.expectedRevision+1)throw Error('STALE_ACK');
   setSnapshot(x);setUncertain(null);setStatus(x.outcome==='Replayed'?
    'همان درخواست، بدون رویداد اضافی بازپخش شد.':
    'رویداد ادعایی ثبت شد؛ ارجاع، تماس واقعی یا نوبت تأیید نشده است.');
  }catch{
   setUncertain(original);setStatus('نتیجه ارسال نامشخص است؛ فقط بازپخش دستی دقیق مجاز است.');
  }finally{setBusy(false);}
 }
 function issue(action:Action){
  if(!snapshot||busy||uncertain)return;
  const origin=snapshot.events[0];
  if(action!=='REQUEST_NONCLINICAL_COORDINATION'&&!origin)return;
  const first=action==='REQUEST_NONCLINICAL_COORDINATION';
  const cmd:Command={requestId:crypto.randomUUID(),
   expectedRevision:snapshot.revision,action,
   memberRef:first?memberRef:origin.memberRef,
   originalMemberRequestRef:first?requestRef:origin.originalMemberRequestRef,
   originalSubmissionRequestId:first?zero:origin.requestId,
   claimedActorRef:first?null:coordinator,
   unverifiedNoteOrDocumentRef:first?null:source};
  void send({ref:caseRef,body:JSON.stringify(cmd)});
 }
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
   <button disabled={busy||!!uncertain||!snapshot||snapshot.revision!==0}
    onClick={()=>issue('REQUEST_NONCLINICAL_COORDINATION')}>
    ثبت درخواست ادعایی هماهنگی</button>
  </fieldset>
  <fieldset><legend>پیگیری‌های تأییدنشده، جدا از تصمیم پزشکی</legend>
   <label>شناسه مدعی هماهنگ‌کننده
    <input dir="ltr" value={coordinator} disabled={busy||!!uncertain}
      onChange={e=>change(setCoordinator,e.target.value)}/></label>
   <label>ارجاع یادداشت یا منبع بالینیِ تأییدنشده
    <input dir="ltr" value={source} disabled={busy||!!uncertain}
      onChange={e=>change(setSource,e.target.value)}/></label>
   <button disabled={busy||!!uncertain||!snapshot||snapshot.revision===0}
    onClick={()=>issue('RECORD_UNVERIFIED_COORDINATOR_CONTACT')}>
    ثبت ادعای تماس هماهنگ‌کننده</button>
   <button disabled={busy||!!uncertain||!snapshot||snapshot.revision===0}
    onClick={()=>issue('ATTACH_UNVERIFIED_CLINICAL_SOURCE')}>
    پیوست ارجاع به منبع بالینیِ تأییدنشده</button>
  </fieldset>
  {uncertain&&<section role="alert"><p>ممکن است رویداد در سرور ثبت شده باشد.
   فرمان جدید قفل است؛ همان متن اصلی و UUID فقط با کلیک جداگانه تکرار می‌شود.</p>
   <button disabled={busy} onClick={()=>void send(uncertain)}>
    بازپخش دستی دقیق فرمان قبلی</button></section>}
  <p role="status" data-testid="care-status">{status}</p>
  {snapshot&&<section className="timeline" role="region" aria-label="تاریخچه هماهنگی ادعایی">
   <p>نسخه پرونده: <strong>{snapshot.revision}</strong></p>
   <p>ارجاع پزشکی، تماس واقعی، نوبت و دسترسی به پرونده: <strong>هیچ‌کدام</strong></p>
   <ol>{snapshot.events.map(e=><li key={e.revision}>
    نسخه {e.revision}: {e.action} — {e.unverifiedNoteOrDocumentRef??'بدون سند'}
   </li>)}</ol>
  </section>}
 </section>;
}
