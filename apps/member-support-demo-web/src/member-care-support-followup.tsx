import {useRef,useState} from 'react';
import {exactSupportSource} from './support-exact-source';
import {exportRecovery,type PendingWrite,type SupportCommand} from './manual-recovery';

// TRN-TECH-205. Local synthetic, unverified Member follow-up after Center
// recorded an UNVERIFIED support note. Not legal, clinical, or an ACK of care.
export type CarePin={caseRef:string;revision:number;memberRef:string;rootRequestId:string};
type FollowUp={revision:number;requestId:string;noteRef:string;claimedActorRef:string};
type Support={contract:'TRN-TECH-075';syntheticOnly:true;outcome:'READ';caseRef:string;
 revision:number;memberRef:string;topic:'MedicalServiceComplaint';
 evidenceRef:string;claimedSubmitterRef:string;originalRequestId:string;
 followUpCount:number;followUps:FollowUp[];
 memberAuthenticated:false;complaintLegallySubmitted:false;
 insurerContacted:false;clinicalReviewCompleted:false;nationalReleaseAllowed:false};
const ref=(v:unknown):v is string=>typeof v==='string'&&
 v.length<=128&&/^test:[A-Za-z0-9_:-]+$/.test(v);
const uuid=(v:unknown):v is string=>typeof v==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);
const api=(s:string)=>'/internal/demo/v1/support/'+encodeURIComponent(s);
function exactRead(value:unknown,caseRef:string,pin:CarePin):value is Support{
 if(!value||typeof value!=='object'||Array.isArray(value))return false;
 const x=value as Partial<Support>;
 if(x.contract!=='TRN-TECH-075'||x.syntheticOnly!==true||
    x.outcome!=='READ'||x.caseRef!==caseRef||!ref(caseRef)||
    x.memberAuthenticated!==false||x.complaintLegallySubmitted!==false||
    x.insurerContacted!==false||x.clinicalReviewCompleted!==false||
    x.nationalReleaseAllowed!==false||
    x.memberRef!==pin.memberRef||x.evidenceRef!==pin.caseRef||
    x.topic!=='MedicalServiceComplaint'||!ref(x.claimedSubmitterRef)||
    !uuid(x.originalRequestId)||!Number.isSafeInteger(x.revision)||
    x.revision!<2||!Array.isArray(x.followUps)||
    x.followUpCount!==x.revision!-1||
    x.followUps.length!==x.followUpCount)return false;
 const seen=new Set<string>([x.originalRequestId.toLowerCase()]);
 for(const [i,event] of x.followUps.entries()){
  if(!event||event.revision!==i+2||!uuid(event.requestId)||
    seen.has(event.requestId.toLowerCase())||
    !ref(event.claimedActorRef)||!ref(event.noteRef))return false;
  seen.add(event.requestId.toLowerCase());
 }
 return true;
}
const sameCare=(a:CarePin,b:CarePin|null)=>!!b&&
 a.caseRef===b.caseRef&&a.memberRef===b.memberRef&&
 a.rootRequestId===b.rootRequestId&&a.revision===b.revision;
const sameSupport=(a:Support,b:Support)=>a.caseRef===b.caseRef&&
 a.revision===b.revision&&a.originalRequestId===b.originalRequestId&&
 a.memberRef===b.memberRef&&a.evidenceRef===b.evidenceRef&&
 a.claimedSubmitterRef===b.claimedSubmitterRef&&
 JSON.stringify(a.followUps)===JSON.stringify(b.followUps);
export function MemberCareSupportFollowUp({source,verifyCare}:{
 source:CarePin|null;verifyCare:(value:unknown,pin:CarePin)=>boolean
}){
 const [caseRef,setCaseRef]=useState('test:care-support-linked');
 const [actor,setActor]=useState('test:claimed-member-followup');
 const [note,setNote]=useState('test:unverified-member-reply');
 const [linked,setLinked]=useState<{care:CarePin;support:Support}|null>(null);
 const [pending,setPending]=useState<PendingWrite|null>(null);
 const [busy,setBusy]=useState(false);
 const [message,setMessage]=useState('برای مشاهده پیگیری ادعایی مرکز، دو ژورنال را به‌صورت دستی بازخوانی کنید.');
 const lock=useRef(false),latest=useRef(source);latest.current=source;
 async function attest(pin:CarePin,expected?:Support):Promise<Support>{
  const care=await fetch('/internal/demo/v1/care-coordination/'+
    encodeURIComponent(pin.caseRef),{cache:'no-store',credentials:'omit'});
  if(!care.ok||!verifyCare(await care.json(),pin)||!sameCare(pin,latest.current))
    throw Error('CARE_NOT_CURRENT');
  const response=await fetch(api(caseRef),{cache:'no-store',credentials:'omit'});
  if(!response.ok)throw Error('SUPPORT_UNAVAILABLE');
  const read:unknown=await response.json();
  if(!exactRead(read,caseRef,pin)||!sameCare(pin,latest.current)||
    (expected&&!sameSupport(read,expected)))throw Error('SUPPORT_NOT_CURRENT');
  return read;
 }
 async function inspect(){
  if(lock.current||pending||!source||!ref(caseRef)||caseRef===source.caseRef)return;
  lock.current=true;setBusy(true);setLinked(null);
  try{
   const support=await attest(source);
   setLinked({care:source,support});
   setMessage('هر دو ژورنال مستقل تطبیق شدند؛ آخرین یادداشت مرکز صرفاً یک ادعای تأییدنشده است.');
  }catch{
   setMessage('هماهنگی یا پیگیری مرکز قابل تأیید نیست؛ هیچ فرمان جدیدی فعال نشد.');
  }finally{lock.current=false;setBusy(false);}
 }
 async function send(original:PendingWrite){
  try{
   const response=await fetch(api(original.caseRef)+'/commands',{
    method:'POST',cache:'no-store',credentials:'omit',
    headers:{'Content-Type':'application/json'},
    body:JSON.stringify(original.command)});
   if(!response.ok)throw Error('UNCERTAIN');
   const ack:unknown=await response.json();
   if(!exactSupportSource(ack,original.caseRef,original.command,'ACK'))
    throw Error('FORGED_ACK');
   const check=await fetch(api(original.caseRef),{
    cache:'no-store',credentials:'omit'});
   if(!check.ok||!exactSupportSource(await check.json(),
      original.caseRef,original.command,'READ'))
    throw Error('UUID_NOT_PERSISTED');
   setPending(null);setLinked(null);
   setMessage('پیگیری ادعایی عضو با UUID اصلی در PostgreSQL تأیید شد؛ هیچ پاسخ یا دریافت خدمت واقعی احراز نشد.');
  }catch{
   setPending(original);
   setMessage('نتیجه ثبت نامشخص است؛ UUID اصلی محفوظ و فرمان جدید مسدود است.');
  }
 }
 async function write(){
  const p=linked;
  if(lock.current||pending||!p||!sameCare(p.care,source)||
     !ref(actor)||!ref(note))return;
  lock.current=true;setBusy(true);
  let original:PendingWrite|null=null;
  try{
   // Recheck each journal at the exact pinned revision before issuing POST.
   await attest(p.care,p.support);
   const command:SupportCommand={
    requestId:crypto.randomUUID(),expectedRevision:p.support.revision,
    action:'FOLLOW_UP_UNVERIFIED',memberRef:p.support.memberRef,
    topic:'MEDICAL_SERVICE_COMPLAINT',
    unverifiedEvidenceRef:p.support.evidenceRef,
    claimedSubmitterRef:p.support.claimedSubmitterRef,
    originalSubmissionRequestId:p.support.originalRequestId,
    claimedFollowUpActorRef:actor,unverifiedFollowUpNoteRef:note
   };
   original={caseRef:p.support.caseRef,command};
   // Freeze original UUID/payload BEFORE POST and retain it for ACK loss.
   setPending(original);
   await send(original);
  }catch{
   if(!original){
    setLinked(null);
    setMessage('نسخه هماهنگی یا پشتیبانی تغییر کرد؛ هیچ POST جدیدی ارسال نشد.');
   }
  }finally{lock.current=false;setBusy(false);}
 }
 async function recover(){
  if(lock.current||!pending)return;
  lock.current=true;setBusy(true);
  const original=pending;
  try{
   const response=await fetch(api(original.caseRef),{
    cache:'no-store',credentials:'omit'});
   if(!response.ok||!exactSupportSource(await response.json(),
      original.caseRef,original.command,'READ'))throw Error('ORIGINAL_NOT_FOUND');
   setPending(null);setLinked(null);
   setMessage('UUID اصلی تنها با GET مستقل تأیید شد؛ هیچ POST جدیدی ارسال نشد.');
  }catch{
   setMessage('UUID اصلی در PostgreSQL قابل تأیید نیست؛ قفل و همان رسید محفوظ ماند.');
  }finally{lock.current=false;setBusy(false);}
 }
 async function replay(){
  if(lock.current||!pending)return;
  lock.current=true;setBusy(true);
  try{await send(pending);}
  finally{lock.current=false;setBusy(false);}
 }
 return <section className="panel" data-testid="member-care-support-followup"
  aria-label="پیگیری دستی عضو پس از یادداشت ادعایی مرکز">
  <h2>پیگیری دستی عضو پس از یادداشت مرکز — صرفاً آزمایشی</h2>
  <p>این نما فقط مرجع ادعایی یک پیگیری مرکز را از ژورنال پشتیبانی
   تطبیق می‌دهد. خواندن یا ثبت، دریافت خدمت، رضایت، ارجاع پزشکی یا
   احراز هویت عضو را اثبات نمی‌کند؛ دو ژورنال تراکنش مشترک ندارند.</p>
  <label>شناسه پرونده پشتیبانی مرتبط با هماهنگی
   <input aria-label="شناسه پشتیبانی برای پیگیری عضو" dir="ltr"
    maxLength={128} disabled={busy||!!pending} value={caseRef}
    onChange={e=>{setCaseRef(e.target.value);setLinked(null);}}/></label>
  <button type="button" disabled={busy||!!pending||!source||!ref(caseRef)}
   onClick={()=>void inspect()}>بازخوانی مستقل هماهنگی و آخرین پیگیری مرکز</button>
  {linked&&sameCare(linked.care,source)&&<section
   data-testid="member-care-support-matched">
   <p>پرونده پشتیبانی <code>{linked.support.caseRef}</code>، نسخه
    {linked.support.revision}، آخرین ادعای پیگیری مرکز:
    <code>{linked.support.followUps[linked.support.followUps.length-1].noteRef}</code>.
    این فقط ثبت آزمایشی در PostgreSQL است.</p>
  </section>}
  <label>پیگیر ادعایی عضو
   <input aria-label="پیگیر ادعایی عضو در هماهنگی" dir="ltr"
    maxLength={100} disabled={busy||!!pending} value={actor}
    onChange={e=>setActor(e.target.value)}/></label>
  <label>مرجع یادداشت تأییدنشده عضو
   <input aria-label="ارجاع یادداشت بازگشت عضو" dir="ltr"
    maxLength={100} disabled={busy||!!pending} value={note}
    onChange={e=>setNote(e.target.value)}/></label>
  <button type="button" disabled={busy||!!pending||!linked||
   !sameCare(linked.care,source)||!ref(actor)||!ref(note)}
   onClick={()=>void write()}>ثبت دستی پیگیری ادعایی عضو با بررسی دوباره دو منبع</button>
  {pending&&<section role="alert" data-testid="member-care-support-uncertain">
   <p>همان UUID اصلی <code>{pending.command.requestId}</code> قفل است.
    رسید با مسیر بازیابی فعلی پشتیبانی عضو سازگار است.</p>
   <textarea aria-label="رسید پیگیری نامطمئن عضو" readOnly
    value={exportRecovery(pending)} rows={5}/>
   <button type="button" disabled={busy} onClick={()=>void recover()}>
    بررسی فقط‌خواندنی UUID پیگیری عضو
   </button>
   <button type="button" disabled={busy} onClick={()=>void replay()}>
    بازپخش دستی دقیق پیگیری عضو
   </button>
  </section>}
  <p role="status" data-testid="member-care-support-followup-status">
   {busy?'در حال بررسی یا ثبت صرفاً آزمایشی':message}</p>
 </section>;
}
