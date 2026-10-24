import {useRef,useState} from 'react';

// Local test-only support worklist; case references are not member identities.
type Row={caseRef:string;revision:number;followUpCount:number};
type Page={
 contract:'TRN-TECH-129';syntheticOnly:true;memberAuthenticated:false;
 complaintLegallySubmitted:false;insurerContacted:false;
 clinicalReviewCompleted:false;nationalReleaseAllowed:false;
 source:'VERIFIED_ISOLATED_POSTGRES_SUPPORT_JOURNAL';
 afterCaseRef:string;topic:'ALL'|'MedicalServiceComplaint'|'InsuranceCoverageDispute';
 limit:number;hasMore:boolean;
 nextAfterCaseRef:string|null;cases:Row[]
};
const testRef=(v:string)=>v.length<=128&&/^test:[A-Za-z0-9_:-]{1,123}$/.test(v);
const PAGE_SIZE=4;
type Topic=Page['topic'];
const TOPICS:Topic[]=['ALL','MedicalServiceComplaint','InsuranceCoverageDispute'];
function validPage(input:unknown,after:string,topic:Topic):input is Page{
 if(!input||typeof input!=='object'||Array.isArray(input))return false;
 const v=input as Partial<Page>;
 if(v.contract!=='TRN-TECH-129'||v.syntheticOnly!==true||
  v.memberAuthenticated!==false||v.complaintLegallySubmitted!==false||
  v.insurerContacted!==false||v.clinicalReviewCompleted!==false||
  v.nationalReleaseAllowed!==false||
  v.source!=='VERIFIED_ISOLATED_POSTGRES_SUPPORT_JOURNAL'||
  v.afterCaseRef!==after||v.topic!==topic||v.limit!==PAGE_SIZE||
  typeof v.hasMore!=='boolean'||!Array.isArray(v.cases)||
  v.cases.length>PAGE_SIZE)return false;
 let previous=after;
 for(const item of v.cases){
  if(!item||typeof item.caseRef!=='string'||!testRef(item.caseRef)||
   item.caseRef<=previous||!Number.isSafeInteger(item.revision)||
   item.revision<1||!Number.isSafeInteger(item.followUpCount)||
   item.followUpCount!==item.revision-1)return false;
  previous=item.caseRef;
 }
 return v.hasMore
  ?v.cases.length===PAGE_SIZE&&v.nextAfterCaseRef===previous
  :v.nextAfterCaseRef===null;
}
export function InternalSupportWorklist({onSelect,disabled}:{
 onSelect:(ref:string)=>void;disabled:boolean
}){
 const [afterInput,setAfterInput]=useState('');
 const [topic,setTopic]=useState<Topic>('ALL');
 const [rows,setRows]=useState<Row[]>([]);
 const [next,setNext]=useState<string|null>(null);
 const [phase,setPhase]=useState<'IDLE'|'LOADING'|'READY'|'DENIED'>('IDLE');
 const epoch=useRef(0);
 function changeTopic(next:Topic){
  epoch.current++;setTopic(next);setAfterInput('');setRows([]);setNext(null);
  setPhase('IDLE');
 }
 function changeInput(value:string){
  epoch.current++;setAfterInput(value);setRows([]);setNext(null);setPhase('IDLE');
 }
 async function read(after:string){
  if(disabled||(after.length>0&&!testRef(after)))return;
  const current=++epoch.current;
  setPhase('LOADING');setRows([]);setNext(null);
  try{
   const selected=topic;
   const url='/internal/demo/v1/support-worklist?afterCaseRef='+
    encodeURIComponent(after)+'&limit='+PAGE_SIZE+'&topic='+encodeURIComponent(selected);
   const res=await fetch(url,{credentials:'omit',cache:'no-store'});
   if(!res.ok)throw Error('UNAVAILABLE');
   const body:unknown=await res.json();
   if(!validPage(body,after,selected))throw Error('UNVERIFIED');
   if(epoch.current!==current)return;
   setRows(body.cases);setNext(body.nextAfterCaseRef);setPhase('READY');
  }catch{
   if(epoch.current!==current)return;
   setRows([]);setNext(null);setPhase('DENIED');
  }
 }
 return <section className="panel" data-testid="internal-support-worklist">
  <h3>فهرست پرونده‌های پیگیری ثبت‌شده — فقط محیط آزمایشی</h3>
  <p>فقط شناسه test: و شماره نسخه از ژورنال تأییدشده PostgreSQL؛
   فهرست نه هویت اشخاص را ثابت می‌کند و نه اجازه دسترسی عملیاتی ایجاد می‌کند.</p>
  <label>موضوع ادعای ثبت‌شده در ژورنال
   <select aria-label="فیلتر موضوع پرونده پشتیبانی" value={topic}
    disabled={disabled||phase==='LOADING'}
    onChange={e=>changeTopic(e.target.value as Topic)}>
    {TOPICS.map(t=><option key={t} value={t}>{t==='ALL'?'همه موضوع‌ها':
      t==='MedicalServiceComplaint'?'نارضایتی خدمت پزشکی (ادعایی)':
      'اختلاف پوشش بیمه (ادعایی)'}</option>)}
   </select>
  </label>
  <p>فیلتر صرفاً برچسب رویداد اولیه آزمایشی است؛
   اولویت درمانی، وضعیت رسیدگی یا استحقاق بیمه محسوب نمی‌شود.</p>
  <label>شروع پس از شناسه آزمایشی (اختیاری)
   <input aria-label="شروع فهرست پس از پرونده" value={afterInput}
    disabled={disabled||phase==='LOADING'} maxLength={128} autoComplete="off"
    onChange={e=>changeInput(e.target.value)}/>
  </label>
  <button type="button" disabled={disabled||phase==='LOADING'||
   (afterInput.length>0&&!testRef(afterInput))}
   onClick={()=>void read(afterInput)}>خواندن فهرست داخلی</button>
  {next&&<button type="button" disabled={disabled||phase==='LOADING'}
   onClick={()=>void read(next)}>صفحه بعد پرونده‌ها</button>}
  <p role="status">{phase==='IDLE'?'هنوز فهرست خوانده نشده است.':
   phase==='LOADING'?'در حال خواندن فهرست':
   phase==='DENIED'?'فهرست قابل تأیید نیست؛ اطلاعات پاک شد.':
   rows.length===0?'پرونده ثبت‌شده‌ای در این صفحه وجود ندارد.':
   'فهرست آزمایشی از پایگاه داده تأیید و خوانده شد.'}</p>
  {phase==='READY'&&<ul data-testid="support-worklist-rows">{rows.map(x=>
   <li key={x.caseRef}>پرونده <code>{x.caseRef}</code> — نسخه {x.revision}،
    پیگیری‌ها {x.followUpCount}
    <button type="button" disabled={disabled}
     onClick={()=>onSelect(x.caseRef)}>انتخاب این پرونده</button>
   </li>)}</ul>}
  <small>بدون احراز هویت، اطلاعات شخصی، حکم درمانی یا انتشار ملی</small>
 </section>;
}
