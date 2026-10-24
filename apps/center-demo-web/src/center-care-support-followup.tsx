import {useRef,useState} from 'react';
import {testRef,valid as validCare} from './care-journal-source';
import {exactSupportSource} from './support-exact-source';
import {makeSupportReceipt,parseSupportCommand} from './support-command-receipt';
import type {PendingSupportWrite,SupportWriteBody} from './support-command-receipt';

// TRN-TECH-204. All identity/linkage is UNVERIFIED, synthetic, manual.
// Separate PostgreSQL GETs are not an atomic transaction or authorization.
export type SupportCase={
 caseRef:string;revision:number;outcome:'READ';topic:string|null;
 memberRef:string|null;evidenceRef:string|null;
 originalRequestId:string|null;claimedSubmitterRef:string|null;
 followUps:{revision:number;requestId:string;noteRef:string;claimedActorRef:string}[]
};
type CarePin={caseRef:string;revision:number;rootRequestId:string;memberRef:string};
const supportRef=(v:unknown):v is string=>typeof v==='string'&&
 /^test:[A-Za-z0-9_:-]+$/.test(v)&&v.length<=128;
const uuid=(v:unknown):v is string=>typeof v==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);
const endpoint=(v:string)=>'/internal/demo/v1/support/'+encodeURIComponent(v);
function sameSupport(x:unknown,s:SupportCase):boolean{
 if(!x||typeof x!=='object'||Array.isArray(x))return false;
 const v=x as Record<string,unknown>;
 if(v.contract!=='TRN-TECH-075'||v.syntheticOnly!==true||v.outcome!=='READ'||
 v.memberAuthenticated!==false||v.complaintLegallySubmitted!==false||
 v.insurerContacted!==false||v.clinicalReviewCompleted!==false||
 v.nationalReleaseAllowed!==false||
 v.caseRef!==s.caseRef||v.revision!==s.revision||
 v.topic!==s.topic||v.memberRef!==s.memberRef||
 v.evidenceRef!==s.evidenceRef||v.originalRequestId!==s.originalRequestId||
 v.claimedSubmitterRef!==s.claimedSubmitterRef||
 !Array.isArray(v.followUps)||v.followUpCount!==s.followUps.length||
 v.followUps.length!==s.revision-1||s.followUps.length!==s.revision-1||
 !uuid(v.originalRequestId)||!supportRef(v.memberRef)||
 !supportRef(v.evidenceRef)||!supportRef(v.claimedSubmitterRef))return false;
 return v.followUps.every((entry:unknown,i:number)=>{
  if(!entry||typeof entry!=='object'||Array.isArray(entry))return false;
  const e=entry as Record<string,unknown>,old=s.followUps[i];
  return !!old&&e.revision===i+2&&e.revision===old.revision&&
   uuid(e.requestId)&&e.requestId===old.requestId&&
   e.noteRef===old.noteRef&&supportRef(e.noteRef)&&
   e.claimedActorRef===old.claimedActorRef&&supportRef(e.claimedActorRef);
 });
}
function careMatch(x:unknown,s:SupportCase,pin?:CarePin):CarePin|null{
 const source=s.evidenceRef;
 if(!source||!testRef(source)||!validCare(x,source)||
    x.outcome!=='READ'||x.revision<1||
    s.topic!=='MedicalServiceComplaint'||s.memberRef!==x.events[0].memberRef||
    !uuid(s.originalRequestId)||!supportRef(s.claimedSubmitterRef))return null;
 const ref={caseRef:source,revision:x.revision,
  rootRequestId:x.events[0].requestId,memberRef:x.events[0].memberRef};
 if(pin&&(ref.caseRef!==pin.caseRef||ref.revision!==pin.revision||
    ref.rootRequestId!==pin.rootRequestId||ref.memberRef!==pin.memberRef))
  return null;
 return ref;
}
export function CenterCareSupportFollowUp({source,onChanged,onPendingChange}:{
 source:SupportCase;onChanged:()=>void;onPendingChange:(v:boolean)=>void
}){
 const [carePin,setCarePin]=useState<CarePin|null>(null);
 const [actor,setActor]=useState('test:claimed-center-followup');
 const [note,setNote]=useState('test:unverified-care-support-note');
 const [pending,setPending]=useState<PendingSupportWrite|null>(null);
 const [busy,setBusy]=useState(false);
 const [status,setStatus]=useState('ابتدا هر دو ژورنال را با اقدام دستی تطبیق دهید.');
 const locked=useRef(false);
 // Distinct from the existing generic support form: a guarded
 // support follow-up based on separately reattested care context.
 async function inspect(){
  if(locked.current||pending||!testRef(source.evidenceRef))return;
  locked.current=true;setBusy(true);setCarePin(null);
  onPendingChange(true);
  try{
   const support=await fetch(endpoint(source.caseRef),{
    cache:'no-store',credentials:'omit'});
   if(!support.ok||!sameSupport(await support.json(),source))
    throw Error('SUPPORT_CHANGED');
   const care=await fetch('/internal/demo/v1/care-coordination/'+
    encodeURIComponent(source.evidenceRef!),{cache:'no-store',credentials:'omit'});
   if(!care.ok)throw Error('CARE_NOT_VERIFIED');
   const pin=careMatch(await care.json(),source);
   if(!pin)throw Error('MEMBER_OR_CARE_MISMATCH');
   setCarePin(pin);
   setStatus('دو ژورنال مستقل تطبیق شدند؛ تنها یک ارتباط ادعایی بین شناسه‌های test: دیده شد.');
  }catch{
   setCarePin(null);
   setStatus('مرجع هماهنگی یا پشتیبانی با ژورنال مستقل تطبیق ندارد؛ هیچ فرمانی مجاز نشد.');
  }finally{
   locked.current=false;setBusy(false);onPendingChange(false);
  }
 }
 async function send(original:PendingSupportWrite){
  try{
   const res=await fetch(endpoint(original.caseRef)+'/commands',{
    method:'POST',cache:'no-store',credentials:'omit',
    headers:{'Content-Type':'application/json'},body:original.body});
   if(!res.ok)throw Error('ACK_UNAVAILABLE');
   const ack:unknown=await res.json();
   if(!exactSupportSource(ack,original.caseRef,original.command,'ACK'))
    throw Error('ACK_MISMATCH');
   const sourceRead=await fetch(endpoint(original.caseRef),{
    cache:'no-store',credentials:'omit'});
   if(!sourceRead.ok||!exactSupportSource(await sourceRead.json(),
      original.caseRef,original.command,'READ'))throw Error('UUID_NOT_PERSISTED');
   setPending(null);setCarePin(null);
   onPendingChange(false);onChanged();
   setStatus('UUID پیگیری تأییدنشده در ژورنال مستقل ثبت شد؛ بازخوانی مرکز لازم است.');
  }catch{
   setPending(original);
   setStatus('نتیجه فرمان نامعلوم است؛ همان UUID برای GET یا بازپخش دقیق قفل است.');
  }
 }
 async function submit(){
  if(locked.current||pending||!carePin||!testRef(actor)||!testRef(note))return;
  locked.current=true;setBusy(true);onPendingChange(true);
  let original:PendingSupportWrite|null=null;
  try{
   const support=await fetch(endpoint(source.caseRef),{
    cache:'no-store',credentials:'omit'});
   if(!support.ok||!sameSupport(await support.json(),source))
    throw Error('SUPPORT_STALE');
   const care=await fetch('/internal/demo/v1/care-coordination/'+
    encodeURIComponent(carePin.caseRef),{cache:'no-store',credentials:'omit'});
   if(!care.ok||!careMatch(await care.json(),source,carePin))
    throw Error('CARE_STALE');
   const command:SupportWriteBody={
    requestId:crypto.randomUUID(),expectedRevision:source.revision,
    action:'FOLLOW_UP_UNVERIFIED',memberRef:source.memberRef!,
    topic:'MEDICAL_SERVICE_COMPLAINT',
    unverifiedEvidenceRef:source.evidenceRef!,
    claimedSubmitterRef:source.claimedSubmitterRef!,
    originalSubmissionRequestId:source.originalRequestId!,
    claimedFollowUpActorRef:actor,unverifiedFollowUpNoteRef:note
   };
   original=parseSupportCommand(source.caseRef,JSON.stringify(command));
   if(!original)throw Error('INVALID_COMMAND');
   setPending(original);
   await send(original);
  }catch{
   if(!original){
    setCarePin(null);
    onPendingChange(false);
    setStatus('نسخه هماهنگی یا پشتیبانی پیش از POST تغییر کرد؛ هیچ پیگیری جدیدی ارسال نشد.');
   }
  }finally{locked.current=false;setBusy(false);}
 }
 async function reconcile(){
  if(locked.current||!pending)return;
  locked.current=true;setBusy(true);
  try{
   const res=await fetch(endpoint(pending.caseRef),{
    cache:'no-store',credentials:'omit'});
   if(!res.ok||!exactSupportSource(await res.json(),
      pending.caseRef,pending.command,'READ'))throw Error('UUID_NOT_FOUND');
   setPending(null);setCarePin(null);
   onPendingChange(false);onChanged();
   setStatus('UUID اصلی پیگیری از ژورنال مستقل بدون POST جدید تأیید شد.');
  }catch{setStatus('همان UUID در PostgreSQL قابل تأیید نیست؛ قفل باقی ماند.');}
  finally{locked.current=false;setBusy(false);}
 }
 async function replay(){
  if(locked.current||!pending)return;
  locked.current=true;setBusy(true);
  try{await send(pending);}finally{locked.current=false;setBusy(false);}
 }
 return <section className="panel" data-testid="center-care-support-followup"
   aria-label="پیگیری دستی پشتیبانی مرتبط با هماهنگی">
  <h3>پیگیری مرکز بر پایه مرجع ادعایی هماهنگی — آزمایشی</h3>
  <p>فقط پرونده‌هایی که به شناسه test: هماهنگی اشاره می‌کنند.
    تطبیق دو PostgreSQL اثبات رضایت، هویت، سند پزشکی، تماس،
    ارجاع یا شکایت قانونی نیست و تراکنش اتمیک بین دو ژورنال نداریم.</p>
  <p>پشتیبانی: <code>{source.caseRef}</code>، نسخه {source.revision}؛
   مرجع هماهنگی ادعایی: <code>{source.evidenceRef}</code></p>
  <button type="button" disabled={busy||!!pending}
    onClick={()=>void inspect()}>تطبیق مستقل دو ژورنال هماهنگی و پشتیبانی</button>
  {carePin&&<p data-testid="center-care-support-matched">
   شناسه و ریشه هماهنگی صرفاً آزمایشی تطبیق یافت:
   نسخه {carePin.revision}، UUID <code>{carePin.rootRequestId}</code>.
   هیچ اختیار واقعی ایجاد نمی‌شود.
  </p>}
  <label>مدعی پیگیری‌کننده مرکز
   <input aria-label="مدعی پیگیری‌کننده مرکز در پشتیبانی" dir="ltr"
    maxLength={100} disabled={busy||!!pending} value={actor}
    onChange={e=>setActor(e.target.value)}/></label>
  <label>مرجع یادداشت پیگیری تأییدنشده
   <input aria-label="مرجع پیگیری مرتبط با هماهنگی" dir="ltr"
    maxLength={100} disabled={busy||!!pending} value={note}
    onChange={e=>setNote(e.target.value)}/></label>
  <button type="button" disabled={busy||!!pending||!carePin||
    !testRef(actor)||!testRef(note)}
    onClick={()=>void submit()}>ثبت دستی پیگیری تأییدنشده با بررسی دو منبع</button>
  {pending&&<div data-testid="center-care-support-uncertain" role="alert">
   <p>همان فرمان UUID <code>{pending.command.requestId}</code> محفوظ است؛
     فرمان تازه مجاز نیست.</p>
   <textarea readOnly rows={5} aria-label="رسید بازیابی پیگیری مرکز"
    value={makeSupportReceipt(pending)}/>
   <button type="button" disabled={busy} onClick={()=>void reconcile()}>
     بررسی فقط‌خواندنی UUID اصلی پیگیری
   </button>
   <button type="button" disabled={busy} onClick={()=>void replay()}>
     بازپخش دستی همان UUID پیگیری
   </button>
  </div>}
  <p role="status" data-testid="center-care-support-status">
   {busy?'در حال بررسی یا ثبت آزمایشی':status}
  </p>
 </section>;
}
