import {useState} from 'react';

// TRN-TECH-137. This is only a manually navigated, revision-pinned,
// technical timeline in the ephemeral single-principal localhost QA host.
type Entry={activityNo:number;requestRef:string;noteRef:string;
 expectedPreviousNo:number;previousSha256:string;activitySha256:string};
type Page={itemRef:string;currentNo:number;expectedCurrentNo:number;
 asOfNo:number;afterNo:number;limit:number;hasMore:boolean;
 nextAfterNo:number|null;events:Entry[]};
const refOK=(v:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(v)&&v.length<=128;
const canonical=(v:string)=>/^(0|[1-9][0-9]*)$/.test(v)&&
 Number.isSafeInteger(Number(v))&&Number(v)<=2147483647;
function verify(raw:unknown,item:string,current:number,asOf:number,after:number):raw is Page{
 if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
 const v=raw as Partial<Page>;
 if(v.itemRef!==item||v.currentNo!==current||
    v.expectedCurrentNo!==current||v.asOfNo!==asOf||
    v.afterNo!==after||v.limit!==2||typeof v.hasMore!=='boolean'||
    !Array.isArray(v.events)||v.events.length>2||
    (v.nextAfterNo!==null&&
     (!Number.isSafeInteger(v.nextAfterNo)||v.nextAfterNo!<=after)))
    return false;
 let ordinal=after;
 let previous:string|null=null;
 for(const e of v.events){
  ordinal++;
  if(!e||e.activityNo!==ordinal||e.expectedPreviousNo!==ordinal-1||
   typeof e.requestRef!=='string'||!refOK(e.requestRef)||
   typeof e.noteRef!=='string'||!refOK(e.noteRef)||
   typeof e.activitySha256!=='string'||!/^[A-F0-9]{64}$/.test(e.activitySha256)||
   (ordinal===1&&e.previousSha256!=='GENESIS')||
   (ordinal>1&&typeof e.previousSha256!=='string')||
   (previous!==null&&e.previousSha256!==previous)||ordinal>asOf)
   return false;
  previous=e.activitySha256;
 }
 const more=ordinal<asOf;
 return v.hasMore===more &&
  (more?v.events.length===2&&v.nextAfterNo===ordinal:
   v.nextAfterNo===null);
}
export function OrgDraftTimeline({itemRef,currentNo,locked}:{
 itemRef:string;currentNo:number;locked:boolean
}){
 const [asOf,setAsOf]=useState(String(currentNo));
 const [page,setPage]=useState<Page|null>(null);
 const [status,setStatus]=useState('نمای تاریخی هنوز از PostgreSQL خوانده نشده است.');
 const [busy,setBusy]=useState(false);
 async function load(after:number,cutoff:number){
  if(locked||busy||!refOK(itemRef)||!Number.isSafeInteger(cutoff)||
   cutoff<0||cutoff>currentNo||!Number.isSafeInteger(after)||
   after<0||after>cutoff)return;
  setBusy(true);setPage(null);
  try{
   const params=new URLSearchParams({afterNo:String(after),limit:'2',
    expectedCurrentNo:String(currentNo),asOfNo:String(cutoff)});
   const response=await fetch('/internal/demo/v1/org-drafts/'+
    encodeURIComponent(itemRef)+'/activity-page?'+params.toString(),
    {credentials:'omit',cache:'no-store'});
   const raw:unknown=await response.json();
   if(!raw||typeof raw!=='object'||Array.isArray(raw))
    throw Error('UNTRUSTED_HISTORY');
   const env=raw as Record<string,unknown>;
   if(env.contract!=='TRN-TECH-135'||
    env.source!=='SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY'||
    env.syntheticOnly!==true||env.organizationAuthenticated!==false||
    env.operatorAuthorized!==false||env.businessStatusChanged!==false||
    env.nationalReleaseAllowed!==false)throw Error('FORGED_AUTHORITY');
   if(response.status===409&&env.outcome==='StaleRevision'){
    setStatus('نسخه فعلی تغییر کرده است؛ بازخوانی کامل پیش‌نویس لازم است.');
    return;
   }
   if(response.status!==200||env.outcome!=='Read'||
    !verify(env.data,itemRef,currentNo,cutoff,after))
    throw Error('BAD_TIMELINE_PAGE');
   setPage(env.data);
   setStatus('خط زمانی نسخه‌مقید از PostgreSQL راستی‌آزمایی و خوانده شد.');
  }catch{setPage(null);setStatus('صفحه تاریخی قابل تأیید نیست؛ رخدادی نمایش داده نمی‌شود.');}
  finally{setBusy(false);}
 }
 const allowed=canonical(asOf)&&Number(asOf)<=currentNo;
 return <section className="panel" data-testid="org-draft-timeline">
  <h2>خط زمانی نسخه‌های پیش‌نویس</h2>
  <p>فقط مراجع فنی test:؛ بدون تغییر وضعیت و بدون تصمیم تأیید.</p>
  <p>نسخه ثبت‌شده هنگام آخرین خواندن: {currentNo}</p>
  <label>نمایش فعالیت‌ها تا نسخه
   <input aria-label="نمایش فعالیت‌ها تا نسخه" dir="ltr"
    value={asOf} disabled={locked||busy}
    onChange={e=>{setAsOf(e.target.value);setPage(null);}}/>
  </label>
  <button type="button" disabled={locked||busy||!allowed}
   onClick={()=>void load(0,Number(asOf))}>مشاهده نسخه تاریخی</button>
  <p role="status">{status}</p>
  {page&&<div data-testid="org-timeline-events">
   <p>تا نسخه {page.asOfNo} | صفحه پس از رخداد {page.afterNo}</p>
   <ol>{page.events.map(e=><li key={e.activityNo}>
    نسخه {e.activityNo} — <code>{e.noteRef}</code>
   </li>)}</ol>
   {page.events.length===0&&<p>در این نسخه، فعالیتی ثبت نشده است.</p>}
   {page.hasMore&&page.nextAfterNo!==null&&
    <button type="button" disabled={locked||busy}
     onClick={()=>void load(page.nextAfterNo!,page.asOfNo)}>صفحه بعد تاریخچه</button>}
  </div>}
 </section>;
}
