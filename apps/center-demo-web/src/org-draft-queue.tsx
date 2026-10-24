import {useState} from 'react';

// Read-only, source-attested organization technical queue. These refs are
// synthetic. The QA host always uses ONE fixed support DB principal.
type QueueRow={itemRef:string;sourceRef:string;activityCount:number;
 lastNoteRef:string|null;lastRequestRef:string|null};
type QueuePage={afterItemRef:string;limit:number;nextAfterItemRef:string|null;
 items:QueueRow[]};
const valid=(v:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(v)&&v.length<=128;
function verified(raw:unknown,after:string):raw is QueuePage{
 if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
 const v=raw as Partial<QueuePage>;
 if(v.afterItemRef!==after||v.limit!==5||!Array.isArray(v.items)||
  v.items.length>5||
  (v.nextAfterItemRef!==null&&
   (typeof v.nextAfterItemRef!=='string'||!valid(v.nextAfterItemRef))))return false;
 let previous=after;
 for(const item of v.items){
  if(!item||typeof item.itemRef!=='string'||!valid(item.itemRef)||
   item.itemRef<=previous||typeof item.sourceRef!=='string'||!valid(item.sourceRef)||
   !Number.isSafeInteger(item.activityCount)||item.activityCount<0||
   (item.activityCount===0&&(item.lastNoteRef!==null||item.lastRequestRef!==null))||
   (item.activityCount>0&&
    (typeof item.lastNoteRef!=='string'||!valid(item.lastNoteRef)||
     typeof item.lastRequestRef!=='string'||!valid(item.lastRequestRef))))return false;
  previous=item.itemRef;
 }
 return v.nextAfterItemRef===null||
  (v.items.length===5&&v.nextAfterItemRef===v.items[4].itemRef);
}
export function OrgDraftQueue({locked,onSelect}:{
 locked:boolean;onSelect:(item:string)=>void
}){
 const [after,setAfter]=useState('');
 const [page,setPage]=useState<QueuePage|null>(null);
 const [status,setStatus]=useState('کارپوشه فنی هنوز از PostgreSQL خوانده نشده است.');
 const [pending,setPending]=useState(false);
 async function load(cursor:string){
  if(locked||pending||(cursor!==''&&!valid(cursor)))return;
  setPending(true);setPage(null);
  try{
   const response=await fetch('/internal/demo/v1/org-draft-queue?afterItemRef='+
    encodeURIComponent(cursor)+'&limit=5',{credentials:'omit',cache:'no-store'});
   const raw:unknown=await response.json();
   if(!response.ok||!raw||typeof raw!=='object'||Array.isArray(raw))
    throw Error('BAD_QUEUE_SOURCE');
   const env=raw as Record<string,unknown>;
   if(env.contract!=='TRN-TECH-135'||
    env.source!=='SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY'||
    env.syntheticOnly!==true||env.organizationAuthenticated!==false||
    env.operatorAuthorized!==false||env.businessStatusChanged!==false||
    env.nationalReleaseAllowed!==false||env.outcome!=='Read'||
    !verified(env.data,cursor))throw Error('UNVERIFIABLE_QUEUE');
   setPage(env.data);setAfter(cursor);
   setStatus('کارپوشه از یک نمای معتبر PostgreSQL خوانده شد؛ هیچ مجوز اپراتوری احراز نشده است.');
  }catch{
   setPage(null);
   setStatus('کارپوشه معتبر نیست؛ داده‌ها نمایش داده نمی‌شوند.');
  }finally{setPending(false);}
 }
 return <section className="panel" data-testid="org-draft-queue">
  <h2>کارپوشه پیش‌نویس‌های آزمایشی</h2>
  <p>نمای یکپارچه فنی از منبع و شمار فعالیت؛ بدون تأیید، اولویت‌بندی یا انتساب سازمانی.</p>
  <label>شروع کارپوشه پس از شناسه
   <input aria-label="شروع کارپوشه پس از شناسه" dir="ltr"
    value={after} disabled={locked||pending}
    onChange={e=>{setAfter(e.target.value);setPage(null);}}/>
  </label>
  <button type="button" disabled={locked||pending||(after!==''&&!valid(after))}
   onClick={()=>void load(after)}>خواندن کارپوشه PostgreSQL</button>
  <p role="status">{status}</p>
  {page&&<div data-testid="org-queue-rows"><ul>
   {page.items.map(row=><li key={row.itemRef}>
    <code>{row.itemRef}</code>
    <span> | تعداد فعالیت: {row.activityCount}</span>
    <span> | آخرین یادداشت: {row.lastNoteRef??'ثبت نشده'}</span>
    <button type="button" disabled={locked||pending}
     onClick={()=>onSelect(row.itemRef)}>انتخاب و بازخوانی پیش‌نویس</button>
   </li>)}
  </ul>
  {page.nextAfterItemRef&&<button type="button" disabled={locked||pending}
   onClick={()=>void load(page.nextAfterItemRef!)}>صفحه بعد کارپوشه</button>}
  </div>}
 </section>;
}
