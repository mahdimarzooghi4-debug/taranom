import {useEffect,useState} from 'react';

// Local test-only UI over the EXISTING .NET + PostgreSQL support journal.
// A readable response is not authentication, complaint submission or approval.
type FollowUp={revision:number;noteRef:string;claimedActorRef:string};
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
  !['MEDICAL_SERVICE_COMPLAINT','INSURANCE_COVERAGE_DISPUTE',null].includes(x.topic??null))
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
  validRef(f.noteRef)&&validRef(f.claimedActorRef));
}
export function InternalSupportRead(){
 const [ref,setRef]=useState('test:local-support');
 const [phase,setPhase]=useState<'IDLE'|'LOADING'|'DENIED'|'READ'>('IDLE');
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);
 const [generation,setGeneration]=useState(0);
 useEffect(()=>{setSnapshot(null);setPhase('IDLE');},[ref]);
 async function read(){
  if(!validRef(ref))return;
  const requestGeneration=generation+1;
  setGeneration(requestGeneration);
  setSnapshot(null);setPhase('LOADING');
  try{
   const response=await fetch('/internal/demo/v1/support/'+encodeURIComponent(ref),
    {credentials:'omit',cache:'no-store'});
   if(!response.ok)throw Error('UNAVAILABLE');
   const body:unknown=await response.json();
   if(!valid(body,ref))throw Error('UNVERIFIABLE');
   setSnapshot(body);setPhase('READ');
  }catch{setSnapshot(null);setPhase('DENIED');}
 }
 return <section className="panel" data-testid="internal-support-read">
  <h2>خواندن پرونده پیگیری داخلی — محیط آزمایشی</h2>
  <p>این نما از همان سرویس .NET و ژورنال PostgreSQL می‌خواند. هیچ هویت،
    شکایت قانونی، پوشش بیمه یا صلاحیت درمانی از این داده‌ها تأیید نمی‌شود.</p>
  <label>شناسه پرونده آزمایشی
   <input value={ref} onChange={e=>setRef(e.target.value)}
    maxLength={128} autoComplete="off" aria-label="شناسه پرونده پیگیری آزمایشی"/>
  </label>
  <button type="button" disabled={!validRef(ref)||phase==='LOADING'}
    onClick={()=>void read()}>خواندن از پایگاه داده محلی</button>
  <p role="status">{phase==='IDLE'?'هنوز خواندن انجام نشده است.':
   phase==='LOADING'?'در حال خواندن':
   phase==='DENIED'?'پاسخ یا منبع قابل تأیید نیست؛ اطلاعات قبلی پاک شد.':
   'وضعیت آزمایشی از پایگاه داده دریافت شد؛ فاقد اعتبار رسمی است.'}</p>
  {snapshot&&<div data-testid="support-read-result">
   <p>پرونده: <code>{snapshot.caseRef}</code> — نسخه {snapshot.revision}</p>
   <p>موضوع: {snapshot.topic??'ثبت نشده'} | پیگیری‌ها: {snapshot.followUpCount}</p>
   <ul>{snapshot.followUps.map(f=><li key={f.revision}>
    پیگیری {f.revision}: <code>{f.noteRef}</code></li>)}</ul>
   <p>هویت تأییدنشده | ثبت قانونی انجام‌نشده | انتشار ملی ممنوع</p>
  </div>}
 </section>;
}
