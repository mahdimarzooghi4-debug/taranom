import {useRef,useState} from 'react';
import {exactSupportSource} from './support-exact-source';
import {exportRecovery,type PendingWrite,type SupportCommand} from './manual-recovery';

// TRN-TECH-203 — unverified cross-journal pointer, NOT a legal complaint,
// clinical care, provider assignment or permission to access patient data.
export type CarePin={caseRef:string;revision:number;memberRef:string;rootRequestId:string};
const ref=(x:string)=>/^test:[A-Za-z0-9_:-]+$/.test(x)&&x.length<=100;
const supportRef=(x:string)=>/^test:[A-Za-z0-9_:-]+$/.test(x)&&x.length<=128;
const endpoint=(x:string)=>'/internal/demo/v1/support/'+encodeURIComponent(x);
function emptySupport(raw:unknown,caseRef:string):boolean{
 if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
 const x=raw as Record<string,unknown>;
 return x.contract==='TRN-TECH-075'&&x.syntheticOnly===true&&
  x.outcome==='READ'&&x.caseRef===caseRef&&x.revision===0&&
  x.followUpCount===0&&Array.isArray(x.followUps)&&x.followUps.length===0&&
  x.memberRef===null&&x.topic===null&&x.originalRequestId===null&&
  x.evidenceRef===null&&x.claimedSubmitterRef===null&&
  x.memberAuthenticated===false&&x.complaintLegallySubmitted===false&&
  x.insurerContacted===false&&x.clinicalReviewCompleted===false&&
  x.nationalReleaseAllowed===false;
}
export function CareSupportContinuation({source,verifyCare}:{
 source:CarePin|null;verifyCare:(v:unknown,pin:CarePin)=>boolean
}){
 const [supportCase,setSupportCase]=useState(()=>
  'test:care-support-'+crypto.randomUUID().replaceAll('-',''));
 const [submitter,setSubmitter]=useState('test:claimed-member-submitter');
 const [pending,setPending]=useState<PendingWrite|null>(null);
 const [busy,setBusy]=useState(false);
 const [status,setStatus]=useState('هیچ درخواست پشتیبانی آزمایشی ثبت نشده است.');
 const lock=useRef(false),latest=useRef(source);latest.current=source;
 const same=(pin:CarePin)=>!!latest.current&&
  pin.caseRef===latest.current.caseRef&&
  pin.revision===latest.current.revision&&
  pin.memberRef===latest.current.memberRef&&
  pin.rootRequestId===latest.current.rootRequestId;
 async function exactSend(original:PendingWrite){
  try{
   const result=await fetch(endpoint(original.caseRef)+'/commands',{
    method:'POST',cache:'no-store',credentials:'omit',
    headers:{'Content-Type':'application/json'},body:JSON.stringify(original.command)});
   if(!result.ok)throw Error('UNCERTAIN_POST');
   const ack:unknown=await result.json();
   if(!exactSupportSource(ack,original.caseRef,original.command,'ACK'))
    throw Error('FORGED_ACK');
   const read=await fetch(endpoint(original.caseRef),{cache:'no-store',credentials:'omit'});
   if(!read.ok)throw Error('READ_FAILED');
   const journal:unknown=await read.json();
   if(!exactSupportSource(journal,original.caseRef,original.command,'READ'))
    throw Error('UUID_NOT_COMMITTED');
   setPending(null);
   setStatus('UUID اصلی درخواست پشتیبانی در PostgreSQL مستقل تأیید شد؛ هیچ شکایت قانونی یا خدمت واقعی ثبت نشد.');
  }catch{
   setPending(original);
   setStatus('نتیجه POST نامطمئن است؛ فقط GET مستقل یا بازپخش دقیق همان UUID مجاز است.');
  }
 }
 async function create(){
  const pin=source;
  if(lock.current||pending||!pin||pin.revision<1||!ref(pin.caseRef)||
   !ref(pin.memberRef)||!supportRef(supportCase)||!ref(submitter)||
   supportCase===pin.caseRef)return;
  lock.current=true;setBusy(true);
  let original:PendingWrite|null=null;
  try{
   const care=await fetch('/internal/demo/v1/care-coordination/'+
    encodeURIComponent(pin.caseRef),{cache:'no-store',credentials:'omit'});
   if(!care.ok||!verifyCare(await care.json(),pin)||!same(pin))
    throw Error('CARE_CHANGED');
   const support=await fetch(endpoint(supportCase),{cache:'no-store',credentials:'omit'});
   if(!support.ok||!emptySupport(await support.json(),supportCase)||!same(pin))
    throw Error('SUPPORT_NOT_NEW_OR_CARE_CHANGED');
   const command:SupportCommand={requestId:crypto.randomUUID(),expectedRevision:0,
    action:'SUBMIT_UNVERIFIED',memberRef:pin.memberRef,topic:'MEDICAL_SERVICE_COMPLAINT',
    unverifiedEvidenceRef:pin.caseRef,claimedSubmitterRef:submitter,
    originalSubmissionRequestId:'00000000-0000-0000-0000-000000000000',
    claimedFollowUpActorRef:null,unverifiedFollowUpNoteRef:null};
   original={caseRef:supportCase,command};
   // Preserve original BEFORE any POST can have an ambiguous outcome.
   setPending(original);
   await exactSend(original);
  }catch{
   if(!original)setStatus('دو ژورنال یا نسخه دقیق هماهنگی تأیید نشد؛ هیچ POST ارسال نشد.');
  }finally{lock.current=false;setBusy(false);}
 }
 async function inspect(){
  if(lock.current||!pending)return;
  lock.current=true;setBusy(true);
  const original=pending;
  try{
   const read=await fetch(endpoint(original.caseRef),{cache:'no-store',credentials:'omit'});
   if(!read.ok||!exactSupportSource(await read.json(),original.caseRef,original.command,'READ'))
    throw Error('MISSING');
   setPending(null);
   setStatus('UUID اصلی با GET مستقل تأیید شد؛ بدون POST جدید. ارتباط دو پرونده ادعایی است.');
  }catch{setStatus('UUID اصلی قابل تأیید نیست؛ قفل و رسید بازیابی باقی ماند.');}
  finally{lock.current=false;setBusy(false);}
 }
 async function retry(){
  if(lock.current||!pending)return;
  lock.current=true;setBusy(true);
  try{await exactSend(pending);}
  finally{lock.current=false;setBusy(false);}
 }
 return <section data-testid="care-support-continuation" className="panel"
   aria-label="ادامه دستی هماهنگی در پشتیبانی آزمایشی">
  <h2>ادامه دستی هماهنگی در پشتیبانی — فقط آزمایش</h2>
  <p>فقط یک درخواست پشتیبانیِ تأییدنشده در ژورنال مجزا ثبت می‌شود.
   مرجع شاهد، شناسه test: پرونده هماهنگی است؛ این ادعا سند پزشکی،
   شکایت قانونی، رضایت، ارجاع یا رسید خدمت نیست.</p>
  <label>شناسه پرونده پشتیبانی آزمایشی
   <input aria-label="شناسه پشتیبانی برگرفته از هماهنگی" dir="ltr"
    maxLength={128} disabled={busy||!!pending} value={supportCase}
    onChange={e=>setSupportCase(e.target.value)}/></label>
  <label>ثبت‌کننده ادعایی درخواست
   <input aria-label="ثبت‌کننده ادعایی ادامه پشتیبانی" dir="ltr"
    maxLength={100} disabled={busy||!!pending} value={submitter}
    onChange={e=>setSubmitter(e.target.value)}/></label>
  {source&&<p>نسخه هماهنگی آزمایشی: {source.revision}؛
   مرجع <code>{source.caseRef}</code>. دو GET پیش از POST،
   تراکنش مشترک یا انتقال اختیار ایجاد نمی‌کنند.</p>}
  <button type="button" disabled={busy||!!pending||!source||
   !supportRef(supportCase)||!ref(submitter)}
   onClick={()=>void create()}>ثبت دستی پشتیبانی ادعایی از هماهنگی</button>
  {pending&&<div data-testid="care-support-uncertain" role="alert">
   <p>فرمان با UUID <code>{pending.command.requestId}</code> قفل است.
    رسید زیر در مسیر بازیابی موجود پشتیبانی عضو قابل استفاده است.</p>
   <textarea aria-label="رسید بازیابی پشتیبانی هماهنگی" readOnly rows={5}
    value={exportRecovery(pending)}/>
   <button type="button" disabled={busy} onClick={()=>void inspect()}>
    بررسی فقط‌خواندنی UUID پشتیبانی در PostgreSQL
   </button>
   <button type="button" disabled={busy} onClick={()=>void retry()}>
    بازپخش دقیق UUID نامطمئن پشتیبانی
   </button>
  </div>}
  <p role="status" data-testid="care-support-status">{busy?
   'در حال بازخوانی منبع یا ثبت آزمایشی':status}</p>
  <small>بدون شناسه واقعی، خودکارسازی، پرداخت، هوش مصنوعی یا خدمت درمانی.</small>
 </section>;
}
