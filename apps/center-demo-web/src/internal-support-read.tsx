import {useEffect,useRef,useState} from 'react';
import {InternalSupportHistory} from './internal-support-history';
import {InternalSupportWrite} from './internal-support-write';
import {InternalSupportWorklist} from './internal-support-worklist';

// Local test-only UI over the EXISTING .NET + PostgreSQL support journal.
// A readable response is not authentication, complaint submission or approval.
type FollowUp={revision:number;requestId:string;noteRef:string;claimedActorRef:string};
type Snapshot={
 contract:'TRN-TECH-075';syntheticOnly:true;memberAuthenticated:false;
 complaintLegallySubmitted:false;insurerContacted:false;
 clinicalReviewCompleted:false;nationalReleaseAllowed:false;
 caseRef:string;revision:number;outcome:'READ';
 topic:string|null;memberRef:string|null;originalRequestId:string|null;
 evidenceRef:string|null;claimedSubmitterRef:string|null;
 followUpCount:number;followUps:FollowUp[]
};
const validRef=(ref:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(ref)&&ref.length<=128;
function valid(value:unknown,ref:string):value is Snapshot{
 if(!value||typeof value!=='object')return false;
 const x=value as Partial<Snapshot>;
 if(x.contract!=='TRN-TECH-075'||x.syntheticOnly!==true||
  x.memberAuthenticated!==false||x.complaintLegallySubmitted!==false||
  x.insurerContacted!==false||x.clinicalReviewCompleted!==false||
  x.nationalReleaseAllowed!==false||x.caseRef!==ref||x.outcome!=='READ'||
  !Number.isSafeInteger(x.revision)||x.revision!<0||
  !Number.isSafeInteger(x.followUpCount)||x.followUpCount!<0||
  !Array.isArray(x.followUps)||x.followUps.length!==x.followUpCount||
  !['MedicalServiceComplaint','InsuranceCoverageDispute',null].includes(x.topic??null))
  return false;
 const refs=[x.memberRef,x.evidenceRef,x.claimedSubmitterRef];
 if(refs.some(v=>v!==null&&(typeof v!=='string'||!validRef(v))))return false;
 if(x.revision===0)
  return x.topic===null&&refs.every(v=>v===null)&&
   x.originalRequestId===null&&x.followUps.length===0;
 if(!refs.every(v=>typeof v==='string'&&validRef(v))||
  typeof x.originalRequestId!=='string'||!/^[0-9a-f-]{36}$/i.test(x.originalRequestId)||
  x.followUpCount!==x.revision!-1)return false;
 return x.followUps.every((f,i)=>f&&f.revision===i+2&&
  typeof f.requestId==='string'&&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(f.requestId)&&
  validRef(f.noteRef)&&validRef(f.claimedActorRef));
}
export function InternalSupportRead(){
 const [ref,setRef]=useState('test:local-support');
 const [phase,setPhase]=useState<'IDLE'|'LOADING'|'DENIED'|'READ'>('IDLE');
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);
 const [pendingWrite,setPendingWrite]=useState(false);
 const [receipt,setReceipt]=useState('');
 const [handoffStatus,setHandoffStatus]=useState(
   'رسیدی وارد نشده است؛ ورود رسید مجوز پرونده یا درمان نیست.');
 const epoch=useRef(0);
 useEffect(()=>()=>{epoch.current++;},[]);
 function changeRef(value:string){
  epoch.current++;setRef(value);setSnapshot(null);setPhase('IDLE');
  setHandoffStatus('انتخاب پرونده تغییر کرد؛ رسید پیشین دیگر اثبات هیچ تطبیقی نیست.');
 }
 async function acceptHandoff(){
  if(pendingWrite||phase==='LOADING'||receipt.length===0||
     receipt.length>1100)return;
  const generation=++epoch.current;
  setSnapshot(null);setPhase('LOADING');
  try{
   const data:unknown=JSON.parse(receipt);
   if(!data||typeof data!=='object'||Array.isArray(data))
     throw Error('INVALID_RECEIPT');
   const x=data as Record<string,unknown>;
   const keys=['format','version','caseRef','memberRef','topic',
     'evidenceRef','originalRequestId','revision'];
   if(Object.keys(x).length!==keys.length||
      Object.keys(x).some(k=>!keys.includes(k))||
      x.format!=='TRN-TECH-180-TEST-SUPPORT-HANDOFF'||x.version!==1||
      typeof x.caseRef!=='string'||!validRef(x.caseRef)||
      typeof x.memberRef!=='string'||!validRef(x.memberRef)||
      typeof x.evidenceRef!=='string'||!validRef(x.evidenceRef)||
      !['MedicalServiceComplaint','InsuranceCoverageDispute'].includes(String(x.topic))||
      typeof x.originalRequestId!=='string'||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x.originalRequestId)||
      !Number.isSafeInteger(x.revision)||Number(x.revision)<1)
        throw Error('INVALID_FIELDS');
   // This is the original canonical, restricted, read-only support source;
   // the untrusted receipt cannot authorize this GET or any future writes.
   const res=await fetch('/internal/demo/v1/support/'+encodeURIComponent(x.caseRef),
     {cache:'no-store',credentials:'omit'});
   if(!res.ok)throw Error('SOURCE_UNAVAILABLE');
   const raw:unknown=await res.json();
   if(!valid(raw,x.caseRef)||raw.revision!==x.revision||
      raw.memberRef!==x.memberRef||raw.topic!==x.topic||
      raw.evidenceRef!==x.evidenceRef||
      raw.originalRequestId!==x.originalRequestId)
      throw Error('SOURCE_MISMATCH');
   if(epoch.current!==generation)return;
   setRef(x.caseRef);setSnapshot(raw);setPhase('READ');setReceipt('');
   setHandoffStatus('همان پرونده و نسخه دقیق از PostgreSQL مستقل خوانده شد؛ هیچ انتقال اختیار رخ نداد.');
  }catch{
   if(epoch.current!==generation)return;
   setSnapshot(null);setPhase('DENIED');
   setHandoffStatus('رسید یا نسخه با ژورنال تطبیق نداشت؛ هیچ پرونده‌ای انتخاب یا تأیید نشد.');
  }
 }
 async function read(){
  if(!validRef(ref))return;
  const requestGeneration=++epoch.current;
  setSnapshot(null);setPhase('LOADING');
  try{
   const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(ref),
    {credentials:'omit',cache:'no-store'});
   if(!response.ok)throw Error('UNAVAILABLE');
   const body:unknown=await response.json();
   if(!valid(body,ref))throw Error('UNVERIFIABLE');
   if(epoch.current===requestGeneration){setSnapshot(body);setPhase('READ');}
  }catch{if(epoch.current===requestGeneration){setSnapshot(null);setPhase('DENIED');}}
 }
 return <section className="panel" data-testid="internal-support-read">
  <h2>خواندن پرونده پیگیری داخلی — محیط آزمایشی</h2>
  <p>این نما از همان سرویس .NET و ژورنال PostgreSQL می‌خواند. هیچ هویت،
    شکایت قانونی، پوشش بیمه یا صلاحیت درمانی از این داده‌ها تأیید نمی‌شود.</p>
  <InternalSupportWorklist disabled={pendingWrite} onSelect={changeRef}/>
  <section data-testid="center-support-handoff" aria-label="پذیرش دستی مرجع پرونده آزمایشی عضو">
   <h3>دریافت دستی مرجع پشتیبانی از محیط عضو</h3>
   <p>تنها برای تست مشترک دو رابط است؛ هیچ احراز هویت، ارجاع درمانی،
     اجازه اشتراک‌گذاری سلامت، شکایت رسمی یا اختیار سازمانی ایجاد نمی‌شود.
     تطبیق باید دوباره از ژورنال PostgreSQL انجام شود.</p>
   <label>رسید آزمایشی عضو
    <textarea aria-label="ورود رسید آزمایشی عضو" dir="ltr" rows={5}
      value={receipt} maxLength={1100} disabled={pendingWrite||phase==='LOADING'}
      onChange={e=>setReceipt(e.target.value)} spellCheck={false}/>
   </label>
   <button type="button" disabled={pendingWrite||phase==='LOADING'||
       receipt.length===0||receipt.length>1100}
     onClick={()=>void acceptHandoff()}>راستی‌آزمایی دستی رسید از ژورنال</button>
   <p data-testid="center-support-handoff-status" role="status">{handoffStatus}</p>
  </section>
  <label>شناسه پرونده آزمایشی
   <input value={ref} disabled={pendingWrite} onChange={e=>changeRef(e.target.value)}
    maxLength={128} autoComplete="off" aria-label="شناسه پرونده پیگیری آزمایشی"/>
  </label>
  <button type="button" disabled={!validRef(ref)||phase==='LOADING'||pendingWrite}
    onClick={()=>void read()}>خواندن از پایگاه داده محلی</button>
  <p role="status" data-testid="support-read-status">{phase==='IDLE'?'هنوز خواندن انجام نشده است.':
   phase==='LOADING'?'در حال خواندن':
   phase==='DENIED'?'پاسخ یا منبع قابل تأیید نیست؛ اطلاعات قبلی پاک شد.':
   'وضعیت آزمایشی از پایگاه داده دریافت شد؛ فاقد اعتبار رسمی است.'}</p>
  {snapshot&&snapshot.revision>1&&snapshot.followUpCount>0&&!pendingWrite&&
    snapshot.caseRef===ref&&<section className="panel"
      data-testid="center-member-followup-ticket"
      aria-label="بازگشت رسید پیگیری آزمایشی به عضو">
      <h3>رسید دستی آخرین پیگیری ثبت‌شده برای عضو آزمایشی</h3>
      <p>فقط رویداد تأییدنشده همان ژورنال؛ نه پاسخ درمانی، بیمه‌ای،
        احراز هویت یا تأیید رسیدگی. این متن مجوز دسترسی یا انتشار نیست.</p>
      <textarea readOnly dir="ltr" rows={10}
        aria-label="رسید خروجی آخرین پیگیری مرکز" spellCheck={false}
        value={JSON.stringify({
          format:'TRN-TECH-182-TEST-FOLLOWUP-RETURN',version:1,
          caseRef:snapshot.caseRef,memberRef:snapshot.memberRef,
          topic:snapshot.topic,evidenceRef:snapshot.evidenceRef,
          originalRequestId:snapshot.originalRequestId,
          revision:snapshot.revision,
          followUpRequestId:snapshot.followUps[snapshot.followUps.length-1].requestId,
          noteRef:snapshot.followUps[snapshot.followUps.length-1].noteRef,
          claimedActorRef:snapshot.followUps[snapshot.followUps.length-1].claimedActorRef
        },null,2)}/>
    </section>}
  {snapshot&&<div data-testid="support-read-result">
   <p>پرونده: <code>{snapshot.caseRef}</code> — نسخه {snapshot.revision}</p>
   <p>موضوع: {snapshot.topic??'ثبت نشده'} | پیگیری‌ها: {snapshot.followUpCount}</p>
   <ul>{snapshot.followUps.map(f=><li key={f.revision}>
    پیگیری {f.revision}: <code>{f.noteRef}</code></li>)}</ul>
   <p>هویت تأییدنشده | ثبت قانونی انجام‌نشده | انتشار ملی ممنوع</p>
   <InternalSupportWrite key={snapshot.caseRef+':'+snapshot.revision}
    caseRef={snapshot.caseRef} existing={snapshot} onChanged={()=>void read()}
    onPendingChange={setPendingWrite}/>
   <InternalSupportHistory key={snapshot.caseRef+':'+snapshot.revision}
    caseRef={snapshot.caseRef} revision={snapshot.revision}/>
  </div>}
 </section>;
}
