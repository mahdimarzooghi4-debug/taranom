import {useRef,useState} from 'react';

// TRN-TECH-184: CI-only source-linked coordination claims. Not a clinician
// login, patient referral, treatment, appointment or approved contact.
type Action='RequestNonclinicalCoordination'|
 'RecordUnverifiedCoordinatorContact'|'AttachUnverifiedClinicalSourceReference';
type Event={revision:number;action:Action;requestId:string;memberRef:string;
 originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:string|null;unverifiedNoteOrDocumentRef:string|null};
type Snapshot={contract:'TRN-TECH-115';syntheticOnly:true;unverifiedClaimsOnly:true;
 caseRef:string;outcome:'READ'|'RecordedUnverified'|'Replayed';
 revision:number;events:Event[];
 realMemberAuthenticated:false;realCoordinatorVerified:false;
 clinicalSourceVerified:false;clinicianAuthorized:false;
 referralOrdered:false;referralDelivered:false;appointmentBooked:false;
 treatmentStarted:false;medicalRecordAccessGranted:false;
 emergencyServiceDispatched:false;consentGranted:false;
 walletMutated:false;nationalReleaseAllowed:false};
type Original={ref:string;body:string;requestId:string;
 expectedRevision:number;memberRef:string;originRef:string;
 originalId:string;actor:string;note:string};
const deny=['realMemberAuthenticated','realCoordinatorVerified',
 'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
 'referralDelivered','appointmentBooked','treatmentStarted',
 'medicalRecordAccessGranted','emergencyServiceDispatched','consentGranted',
 'walletMutated','nationalReleaseAllowed'] as const;
const testRef=(x:unknown):x is string=>typeof x==='string'&&
 x.length<=100&&/^test:[A-Za-z0-9_:-]+$/.test(x);
const uuid=(x:unknown):x is string=>typeof x==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(x);
const ZERO='00000000-0000-0000-0000-000000000000';
function valid(x:unknown,ref:string):x is Snapshot{
 if(!x||typeof x!=='object'||Array.isArray(x))return false;
 const s=x as Partial<Snapshot>;
 if(s.contract!=='TRN-TECH-115'||s.syntheticOnly!==true||
 s.unverifiedClaimsOnly!==true||s.caseRef!==ref||
 !['READ','RecordedUnverified','Replayed'].includes(String(s.outcome))||
 !Number.isSafeInteger(s.revision)||s.revision!<0||
 !Array.isArray(s.events)||s.events.length!==s.revision||
 !deny.every(k=>s[k]===false))return false;
 let first:Event|undefined;
 for(const [i,event] of s.events.entries()){
  if(!event||event.revision!==i+1||!uuid(event.requestId)||
    !testRef(event.memberRef)||!testRef(event.originalMemberRequestRef))
   return false;
  if(i===0){
   if(event.action!=='RequestNonclinicalCoordination'||
     event.originalSubmissionRequestId!==ZERO||
     event.claimedActorRef!==null||event.unverifiedNoteOrDocumentRef!==null)
    return false;
   first=event;
  }else if(!first||
   !['RecordUnverifiedCoordinatorContact','AttachUnverifiedClinicalSourceReference'].includes(event.action)||
   event.memberRef!==first.memberRef||
   event.originalMemberRequestRef!==first.originalMemberRequestRef||
   event.originalSubmissionRequestId!==first.requestId||
   !testRef(event.claimedActorRef)||!testRef(event.unverifiedNoteOrDocumentRef))
   return false;
 }
 return new Set(s.events.map(e=>e.requestId)).size===s.events.length;
}
function acknowledged(source:Snapshot,original:Original){
 if(source.revision<original.expectedRevision+1)return false;
 const matches=source.events.filter(e=>e.requestId===original.requestId);
 return matches.length===1&&
 matches[0].revision===original.expectedRevision+1&&
 matches[0].action==='RecordUnverifiedCoordinatorContact'&&
 matches[0].memberRef===original.memberRef&&
 matches[0].originalMemberRequestRef===original.originRef&&
 matches[0].originalSubmissionRequestId===original.originalId&&
 matches[0].claimedActorRef===original.actor&&
 matches[0].unverifiedNoteOrDocumentRef===original.note;
}
export function InternalCareCoordination(){
 const [ref,setRef]=useState('test:care-center-qa');
 const [actor,setActor]=useState('test:claimed-coordinator');
 const [note,setNote]=useState('test:claimed-contact-note');
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);
 const [pending,setPending]=useState<Original|null>(null);
 const [busy,setBusy]=useState(false);
 const [status,setStatus]=useState('ابتدا درخواست آزمایشی عضو را از ژورنال بخوانید.');
 const epoch=useRef(0);
 const url=(caseRef:string)=>'/internal/demo/v1/care-coordination/'+encodeURIComponent(caseRef);
 function changeRef(value:string){
  epoch.current++;setRef(value);setSnapshot(null);
  setStatus('شناسه پرونده تغییر کرد؛ منبع قبلی قابل استفاده نیست.');
 }
 async function sourceRead(caseRef:string){
  const response=await fetch(url(caseRef),{cache:'no-store',credentials:'omit'});
  if(!response.ok)throw Error('READ_UNAVAILABLE');
  const raw:unknown=await response.json();
  if(!valid(raw,caseRef)||raw.outcome!=='READ')throw Error('INVALID_SOURCE');
  return raw;
 }
 async function read(){
  if(busy||pending||!testRef(ref))return;
  const generation=++epoch.current;setBusy(true);setSnapshot(null);
  try{
   const fresh=await sourceRead(ref);
   if(epoch.current!==generation)return;
   if(fresh.revision===0)throw Error('ORIGINAL_MISSING');
   setSnapshot(fresh);
   setStatus('درخواست و ادعاهای قبلی از PostgreSQL خوانده شد؛ هیچ هماهنگ‌کننده واقعی احراز نشده است.');
  }catch{
   if(epoch.current===generation)setStatus('درخواست اولیه معتبر پیدا نشد یا منبع نامعتبر است.');
  }finally{setBusy(false);}
 }
 async function verifiedPending(){
  if(busy||!pending)return;
  const original=pending,generation=++epoch.current;
  setBusy(true);setSnapshot(null);
  try{
   const fresh=await sourceRead(original.ref);
   if(!acknowledged(fresh,original))throw Error('ORIGINAL_NOT_COMMITTED');
   if(epoch.current!==generation)return;
   setPending(null);setSnapshot(fresh);
   setStatus('دقیقاً همان ادعای تماس بدون POST تازه از PostgreSQL بازیابی شد.');
  }catch{
   if(epoch.current===generation)setStatus('رویداد دقیق تأیید نشد؛ همان UUID برای بازپخش دستی محفوظ است.');
  }finally{setBusy(false);}
 }
 async function send(original:Original){
  if(busy)return;
  const generation=++epoch.current;setBusy(true);setSnapshot(null);
  try{
   const response=await fetch(url(original.ref)+'/commands',{
    method:'POST',headers:{'Content-Type':'application/json'},
    credentials:'omit',cache:'no-store',body:original.body});
   const raw:unknown=await response.json();
   if(!response.ok||!valid(raw,original.ref)||
      !['RecordedUnverified','Replayed'].includes(raw.outcome)||
      !acknowledged(raw,original))throw Error('UNCERTAIN_ACK');
   // A successful HTTP reply alone is not proof of this exact command.
   const source=await sourceRead(original.ref);
   if(!acknowledged(source,original))throw Error('SOURCE_MISMATCH');
   if(epoch.current!==generation)return;
   setPending(null);setSnapshot(source);
   setStatus('ادعای تماس دقیقاً در ژورنال ثبت و جداگانه تأیید شد؛ تماس واقعی احراز نشده است.');
  }catch{
   if(epoch.current===generation){
    setPending(original);setStatus('نتیجه ثبت نامعلوم است؛ فرمان جدید قفل شد.');
   }
  }finally{setBusy(false);}
 }
 function submit(){
  if(!snapshot||snapshot.caseRef!==ref||pending||busy||
    !testRef(ref)||!testRef(actor)||!testRef(note))return;
  const root=snapshot.events[0];
  if(!root||!uuid(root.requestId))return;
  const requestId=crypto.randomUUID();
  const body=JSON.stringify({
   requestId,expectedRevision:snapshot.revision,
   action:'RECORD_UNVERIFIED_COORDINATOR_CONTACT',
   memberRef:root.memberRef,
   originalMemberRequestRef:root.originalMemberRequestRef,
   originalSubmissionRequestId:root.requestId,
   claimedActorRef:actor,unverifiedNoteOrDocumentRef:note
  });
  const original:Original={ref,body,requestId,
   expectedRevision:snapshot.revision,memberRef:root.memberRef,
   originRef:root.originalMemberRequestRef,
   originalId:root.requestId,actor,note};
  setPending(original);
  void send(original);
 }
 return <section className="panel" data-testid="center-care-coordination">
  <h2>پیگیری دستی هماهنگی غیرتشخیصی — فقط آزمایش</h2>
  <p>این محیط، پرونده درمانی یا حساب عضو نیست. فقط رویدادهای ادعایی
    ثبت‌شده در ژورنال PostgreSQL عضو را با شناسه test: مشاهده می‌کند.
    مجوز دسترسی، ارجاع و تأیید تماس واقعی وجود ندارد.</p>
  <label>شناسه هماهنگی آزمایشی
   <input aria-label="شناسه پرونده هماهنگی مرکز" value={ref} maxLength={100}
    disabled={busy||!!pending} onChange={e=>changeRef(e.target.value)}/>
  </label>
  <button type="button" disabled={busy||!!pending||!testRef(ref)}
    onClick={()=>void read()}>خواندن مستقل هماهنگی عضو</button>
  <p data-testid="center-care-status" role="status">{status}</p>
  {pending&&<section data-testid="center-care-uncertain" role="alert">
   <p>نتیجه فرمان قبلی نامعلوم است؛ هیچ فرمان تازه‌ای صادر نمی‌شود.</p>
   <button type="button" disabled={busy} onClick={()=>void verifiedPending()}>
    بررسی فقط‌خواندنی همان تماس
   </button>
   <button type="button" disabled={busy} onClick={()=>void send(pending)}>
    بازپخش دستی دقیق UUID قبلی
   </button>
  </section>}
  {snapshot&&<section data-testid="center-care-source">
   <p>پرونده: <code>{snapshot.caseRef}</code> — نسخه {snapshot.revision}</p>
   <p>عضو ادعایی: <code>{snapshot.events[0].memberRef}</code>
    | درخواست اولیه: <code>{snapshot.events[0].originalMemberRequestRef}</code></p>
   <ul>{snapshot.events.map(e=><li key={e.requestId}>
    نسخه {e.revision}: {e.action}
    {e.unverifiedNoteOrDocumentRef&&
     <> — ارجاع تأییدنشده <code>{e.unverifiedNoteOrDocumentRef}</code></>}
   </li>)}</ul>
   <label>مرجع هماهنگ‌کننده ادعایی
    <input aria-label="مرجع هماهنگ‌کننده آزمایشی" value={actor}
     maxLength={100} disabled={busy||!!pending}
     onChange={e=>setActor(e.target.value)}/>
   </label>
   <label>مرجع یادداشت تماس ادعایی
    <input aria-label="مرجع یادداشت تماس آزمایشی" value={note}
     maxLength={100} disabled={busy||!!pending}
     onChange={e=>setNote(e.target.value)}/>
   </label>
   <button type="button" disabled={busy||!!pending||!testRef(actor)||!testRef(note)}
    onClick={submit}>ثبت مستقل ادعای تماس هماهنگ‌کننده</button>
   <p>احراز هویت و صلاحیت: خیر | تماس و ارجاع واقعی: خیر |
    درمان و نوبت: خیر | انتشار ملی: ممنوع</p>
  </section>}
 </section>;
}
