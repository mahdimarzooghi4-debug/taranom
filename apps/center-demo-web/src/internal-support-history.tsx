import {useEffect,useRef,useState} from 'react';

type Event={revision:number;action:'SUBMIT_UNVERIFIED'|'FOLLOW_UP_UNVERIFIED';
 noteRef:string|null;hypotheticalOnly:true};
type Page={contract:'TRN-TECH-084';syntheticOnly:true;memberAuthenticated:false;
 complaintLegallySubmitted:false;insurerContacted:false;clinicalReviewCompleted:false;
 nationalReleaseAllowed:false;caseRef:string;currentRevision:number;afterRevision:number;
 hasMore:boolean;nextAfterRevision:number|null;events:Event[]};
const testRef=(v:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(v)&&v.length<=128;
function valid(v:unknown,ref:string,after:number,pinned:number|null):v is Page{
 if(!v||typeof v!=='object')return false;
 const x=v as Partial<Page>;
 if(x.contract!=='TRN-TECH-084'||x.syntheticOnly!==true||
 x.memberAuthenticated!==false||x.complaintLegallySubmitted!==false||
 x.insurerContacted!==false||x.clinicalReviewCompleted!==false||
 x.nationalReleaseAllowed!==false||x.caseRef!==ref||
 !Number.isSafeInteger(x.currentRevision)||x.currentRevision!<0||
 x.afterRevision!==after||!Array.isArray(x.events)||x.events.length>3||
 typeof x.hasMore!=='boolean'||(pinned!==null&&x.currentRevision!==pinned)||
 x.currentRevision!<after)return false;
 for(let i=0;i<x.events.length;i++){
  const e=x.events[i];
  if(!e||e.revision!==after+i+1||e.revision>x.currentRevision!||
  e.hypotheticalOnly!==true||
  (e.revision===1?(e.action!=='SUBMIT_UNVERIFIED'||e.noteRef!==null):
   (e.action!=='FOLLOW_UP_UNVERIFIED'||typeof e.noteRef!=='string'||!testRef(e.noteRef))))
   return false;
 }
 return x.hasMore ? x.events.length===3&&x.nextAfterRevision===after+3&&
  x.nextAfterRevision<x.currentRevision! : x.nextAfterRevision===null&&
  after+x.events.length===x.currentRevision;
}
export function InternalSupportHistory({caseRef,revision}:{caseRef:string;revision:number}){
 const [events,setEvents]=useState<Event[]>([]);
 const [cursor,setCursor]=useState(0);
 const [hasMore,setHasMore]=useState(false);
 const [state,setState]=useState<'IDLE'|'LOADING'|'READY'|'UNAVAILABLE'>('IDLE');
 const epoch=useRef(0);
 useEffect(()=>{epoch.current++;setEvents([]);setCursor(0);setHasMore(false);setState('IDLE');
  return ()=>{epoch.current++;};},[caseRef,revision]);
 async function read(after:number){
  const generation=++epoch.current;
  setState('LOADING');
  if(after===0){setEvents([]);setCursor(0);setHasMore(false);}
  try{
   const url='/internal/demo/v1/support/'+encodeURIComponent(caseRef)+
    '/history?afterRevision='+after+'&limit=3';
   const res=await fetch(url,{credentials:'omit',cache:'no-store'});
   if(!res.ok)throw Error('SOURCE_UNAVAILABLE');
   const raw:unknown=await res.json();
   if(!valid(raw,caseRef,after,revision))throw Error('UNVERIFIABLE_PAGE');
   if(generation!==epoch.current)return;
   setEvents(previous=>after===0?raw.events:[...previous,...raw.events]);
   setCursor(raw.nextAfterRevision??after+raw.events.length);
   setHasMore(raw.hasMore);setState('READY');
  }catch{
   if(generation!==epoch.current)return;
   setEvents([]);setCursor(0);setHasMore(false);setState('UNAVAILABLE');
  }
 }
 return <section aria-label="تاریخچه داخلی پرونده" data-testid="support-history" className="panel">
  <h3>تاریخچه نسخه‌دار پیگیری داخلی</h3>
  <p>صرفاً رخدادهای آزمایشی و تأییدنشده از همان ژورنال PostgreSQL.</p>
  <button type="button" disabled={state==='LOADING'} onClick={()=>void read(0)}>
   بازخوانی تاریخچه پیگیری</button>
  {hasMore&&<button type="button" disabled={state==='LOADING'}
   onClick={()=>void read(cursor)}>صفحه بعد پیگیری‌ها</button>}
  <p role="status">{state==='IDLE'?'تاریخچه خوانده نشده است.':
   state==='LOADING'?'در حال خواندن تاریخچه':
   state==='UNAVAILABLE'?'تاریخچه یا نسخه قابل تأیید نیست؛ داده پاک شد.':
   'تاریخچه آزمایشی از پایگاه داده بازخوانی شد.'}</p>
  {state==='READY'&&<ol data-testid="support-history-events">
   {events.map(e=><li key={e.revision}>نسخه {e.revision}: {e.action}
    {e.noteRef&&<> — <code>{e.noteRef}</code></>}</li>)}
  </ol>}
  <small>بدون احراز هویت، تأیید بیمه، ثبت قانونی یا مجوز انتشار</small>
 </section>;
}
