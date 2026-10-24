import {useState} from 'react';
import {OrgDraftQueue} from './org-draft-queue';
import {OrgDraftTimeline} from './org-draft-timeline';

// TRN-TECH-135: localhost-only technical QA; not an actual operator login.
type Envelope={contract:string;source:string;syntheticOnly:boolean;
 organizationAuthenticated:boolean;operatorAuthorized:boolean;
 businessStatusChanged:boolean;nationalReleaseAllowed:boolean;outcome:string;data:unknown};
type Draft={itemRef:string;sourceRef:string};
type History={itemRef:string;currentNo:number;events:{activityNo:number;requestRef:string;noteRef:string}[]};
type Pending={url:string;body:string;item:string;requestRef:string;expected:number};
const refOk=(v:string)=>/^test:[A-Za-z0-9_:-]{1,123}$/.test(v)&&v.length<=128;
function trusted(v:unknown):v is Envelope{
 if(!v||typeof v!=='object'||Array.isArray(v))return false;
 const x=v as Partial<Envelope>;
 return x.contract==='TRN-TECH-135'&&x.source==='SUPPORT_SCOPE_EPHEMERAL_POSTGRES_ONLY'&&
 x.syntheticOnly===true&&x.organizationAuthenticated===false&&x.operatorAuthorized===false&&
 x.businessStatusChanged===false&&x.nationalReleaseAllowed===false&&typeof x.outcome==='string';
}
function validDraft(v:unknown,item:string):v is Draft{
 if(!v||typeof v!=='object'||Array.isArray(v))return false;
 const x=v as Partial<Draft>;
 return x.itemRef===item&&typeof x.sourceRef==='string'&&refOk(x.sourceRef);
}
function validHistory(v:unknown,item:string):v is History{
 if(!v||typeof v!=='object'||Array.isArray(v))return false;
 const x=v as Partial<History>;
 return x.itemRef===item&&Number.isSafeInteger(x.currentNo)&&
  Array.isArray(x.events)&&x.events.length===x.currentNo&&
  x.events.every((e,i)=>e&&e.activityNo===i+1&&
   typeof e.noteRef==='string'&&refOk(e.noteRef)&&
   typeof e.requestRef==='string'&&refOk(e.requestRef));
}
const path=(i:string)=>'/internal/demo/v1/org-drafts/'+encodeURIComponent(i);
export function OrgDraftQa(){
 const [item,setItem]=useState('test:qa-'+crypto.randomUUID().replaceAll('-',''));
 const [source,setSource]=useState('test:unverified-source');
 const [note,setNote]=useState('test:technical-activity');
 const [cursor,setCursor]=useState('');
 const [draft,setDraft]=useState<Draft|null>(null);
 const [history,setHistory]=useState<History|null>(null);
 const [items,setItems]=useState<Draft[]|null>(null);
 const [next,setNext]=useState<string|null>(null);
 const [pending,setPending]=useState<Pending|null>(null);
 const [busy,setBusy]=useState(false);
 const [status,setStatus]=useState('هنوز PostgreSQL خوانده نشده است.');
 const [listStatus,setListStatus]=useState('فهرست هنوز خوانده نشده است.');
 async function ask(url:string,body?:string){
  const res=await fetch(url,{credentials:'omit',cache:'no-store',
   ...(body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body})});
  const raw:unknown=await res.json();
  if(!trusted(raw))throw Error('UNVERIFIED_RESPONSE');
  return {res,data:raw};
 }
 function select(i:string){if(busy||pending)return;setItem(i);setDraft(null);setHistory(null);}
 async function read(selected:string=item){
  if(busy||pending||!refOk(selected))return;
  setItem(selected);
  setBusy(true);setDraft(null);setHistory(null);
  try{
   const x=await ask(path(selected));
   if(x.res.status===404&&x.data.outcome==='NotFound'){
    setStatus('پیش‌نویس موجود نیست؛ ثبت آزمایشی امکان دارد.');
   }else{
    if(x.res.status!==200||x.data.outcome!=='Read'||!validDraft(x.data.data,selected))
     throw Error('BAD_DRAFT');
    const h=await ask(path(selected)+'/activity');
    if(h.res.status!==200||h.data.outcome!=='Read'||!validHistory(h.data.data,selected))
     throw Error('BAD_HISTORY');
    setDraft(x.data.data);setHistory(h.data.data);
    setStatus('پیش‌نویس و تاریخچه از PostgreSQL خوانده شدند؛ احراز هویت واقعی وجود ندارد.');
   }
  }catch{setDraft(null);setHistory(null);setStatus('داده قابل تأیید نیست؛ وضعیت پاک شد.');}
  finally{setBusy(false);}
 }
 async function create(){
  if(busy||pending||draft||!refOk(item)||!refOk(source))return;
  setBusy(true);
  try{
   const x=await ask('/internal/demo/v1/org-drafts',
    JSON.stringify({itemRef:item,sourceRef:source}));
   if(x.res.status!==200||!['Recorded','Replayed'].includes(x.data.outcome)||
     !validDraft(x.data.data,item)||x.data.data.sourceRef!==source)
    throw Error('UNVERIFIED_WRITE');
   // Replayed CREATE might already have a nonzero activity revision.
   // Never assume genesis after any write; re-read verified PostgreSQL chain.
   const h=await ask(path(item)+'/activity');
   if(h.res.status!==200||h.data.outcome!=='Read'||!validHistory(h.data.data,item))
    throw Error('UNVERIFIED_ACTIVITY_AFTER_CREATE');
   setDraft(x.data.data);setHistory(h.data.data);
   setStatus(x.data.outcome==='Recorded'?'پیش‌نویس ثبت شد و تاریخچه راستی‌آزمایی شد.':
    'درخواست اولیه بدون تکرار بازپخش شد؛ نسخه واقعی PostgreSQL خوانده شد.');
  }catch{setDraft(null);setHistory(null);
   setStatus('نتیجه ایجاد نامشخص است؛ ابتدا همان شناسه را از PostgreSQL بخوانید.');}
  finally{setBusy(false);}
 }
 async function loadHistory(){
  if(busy||pending||!draft)return;
  setBusy(true);setHistory(null);
  try{
   const x=await ask(path(draft.itemRef)+'/activity');
   if(x.res.status!==200||x.data.outcome!=='Read'||!validHistory(x.data.data,draft.itemRef))
    throw Error('BAD_HISTORY');
   setHistory(x.data.data);setStatus('تاریخچه زنجیره‌ای بازخوانی شد.');
  }catch{setStatus('تاریخچه قابل تأیید نیست؛ ثبت تازه ممنوع است.');}
  finally{setBusy(false);}
 }
 // Only an exact immutable journal event at expected+1 proves this
 // specific write, not a 200 response or an overall currentNo alone.
 async function readExactActivity(command:Pending):Promise<History>{
  const x=await ask(path(command.item)+'/activity');
  if(x.res.status!==200||x.data.outcome!=='Read'||
     !validHistory(x.data.data,command.item))throw Error('JOURNAL_INVALID');
  const body=JSON.parse(command.body) as {
   requestRef?:unknown;noteRef?:unknown;expectedPreviousNo?:unknown};
  if(body.requestRef!==command.requestRef||
     body.expectedPreviousNo!==command.expected||
     typeof body.noteRef!=='string'||!refOk(body.noteRef)||
     x.data.data.currentNo<command.expected+1)
    throw Error('COMMAND_NOT_MATCHED');
  const matching=x.data.data.events.filter(e=>e.requestRef===command.requestRef);
  if(matching.length!==1||matching[0].activityNo!==command.expected+1||
     matching[0].noteRef!==body.noteRef)throw Error('JOURNAL_EVENT_MISMATCH');
  return x.data.data;
 }
 async function inspectPending(){
  if(busy||!pending)return;
  setBusy(true);setHistory(null);
  const original=pending;
  try{
   const history=await readExactActivity(original);
   setHistory(history);setPending(null);
   setStatus('همان فعالیت از ژورنال PostgreSQL بدون ارسال POST بازیابی شد.');
  }catch{
   setStatus('رویداد همان درخواست تأیید نشد؛ قفل و بازپخش دستی حفظ شد.');
  }finally{setBusy(false);}
 }
 async function send(command:Pending){
  if(busy)return;
  setPending(command);setBusy(true);
  try{
   const x=await ask(command.url,command.body);
   if(!x.data.data||typeof x.data.data!=='object')throw Error('BAD_WRITE');
   const val=x.data.data as {itemRef?:unknown;currentNo?:unknown};
   if(val.itemRef!==command.item||!Number.isSafeInteger(val.currentNo))throw Error('BAD_ID');
   const accepted=x.res.status===200&&['Recorded','Replayed'].includes(x.data.outcome)&&
    typeof val.currentNo==='number'&&val.currentNo>=command.expected+1&&
    (x.data.outcome!=='Recorded'||val.currentNo===command.expected+1);
   const denied=x.res.status===409&&['StaleRevision','ConflictingReplay'].includes(x.data.outcome);
   if(!accepted&&!denied)throw Error('UNCERTAIN');
   if(denied){
    setPending(null);setHistory(null);
    setStatus('نسخه یا درخواست ناسازگار است؛ تاریخچه را دوباره بخوانید.');
   }else{
    // The read comes from the existing owner-scoped PostgreSQL journal.
    // Do not clear uncertainty before exact UUID-like requestRef+note match.
    const verified=await readExactActivity(command);
    setHistory(verified);setPending(null);
    setStatus(x.data.outcome==='Recorded'?
      'فعالیت فنی ثبت شد؛ رویداد دقیق از PostgreSQL تأیید شد.':
      'همان درخواست قبلی بازپخش شد؛ یادداشت دقیق در ژورنال تأیید شد.');
   }
  }catch{
   setHistory(null);
   setStatus('نتیجه ثبت نامعلوم است؛ فقط بازپخش همان درخواست مجاز است.');
  }finally{setBusy(false);}
 }
 function append(){
  if(!draft||!history||busy||pending||!refOk(note))return;
  const requestRef='test:req-'+crypto.randomUUID().replaceAll('-','');
  const command={url:path(item)+'/activity',item,requestRef,expected:history.currentNo,
   body:JSON.stringify({requestRef,noteRef:note,expectedPreviousNo:history.currentNo})};
  void send(command);
 }
 async function list(after:string){
  if(busy||pending||(after!==''&&!refOk(after)))return;
  setItems(null);setBusy(true);
  try{
   const x=await ask('/internal/demo/v1/org-drafts?afterItemRef='+encodeURIComponent(after)+'&limit=5');
   if(x.res.status!==200||x.data.outcome!=='Read'||!x.data.data||
     typeof x.data.data!=='object')throw Error('BAD_LIST');
   const p=x.data.data as {afterItemRef?:unknown;limit?:unknown;items?:unknown;
    nextAfterItemRef?:unknown};
   if(p.afterItemRef!==after||p.limit!==5||!Array.isArray(p.items)||
    p.items.length>5||!p.items.every(v=>
      !!v&&typeof v.itemRef==='string'&&refOk(v.itemRef)&&
      typeof v.sourceRef==='string'&&refOk(v.sourceRef))||
    (p.nextAfterItemRef!==null&&
     (typeof p.nextAfterItemRef!=='string'||!refOk(p.nextAfterItemRef))))
    throw Error('BAD_PAGE');
   setItems(p.items as Draft[]);setNext(p.nextAfterItemRef as string|null);
   setCursor(after);setListStatus('فهرست با منبع PostgreSQL آزمایشی خوانده شد.');
  }catch{setListStatus('فهرست قابل تأیید نیست؛ ردیفی نمایش داده نمی‌شود.');}
  finally{setBusy(false);}
 }
 return <div dir="rtl" data-testid="org-draft-qa">
  <header><h1>ترنم | کاربرگ فنی سازمان حمایتی</h1>
   <p>فقط localhost و شناسه‌های test:؛ هیچ هویت و اختیار سازمانی احراز نشده است.</p></header>
  <main>
   <section className="notice" role="note"><strong>فقط محیط فنی — DENIED_BY_DEFAULT</strong>
    <p>اتصال PostgreSQL محدود به نقش آزمایشی ثابت است؛ هیچ انتخاب سازمان از مرورگر مجاز نیست.</p></section>
   <section className="panel"><h2>پیش‌نویس سازمانی آزمایشی</h2>
    <div className="fields"><label>شناسه پیش‌نویس آزمایشی<input aria-label="شناسه پیش‌نویس آزمایشی"
     dir="ltr" value={item} disabled={busy||!!pending} onChange={e=>select(e.target.value)}/></label>
    <label>ارجاع منبع تأییدنشده<input aria-label="ارجاع منبع تأییدنشده"
     dir="ltr" value={source} disabled={busy||!!pending||!!draft}
     onChange={e=>setSource(e.target.value)}/></label></div>
    <div className="actions">
     <button disabled={busy||!!pending||!refOk(item)} onClick={()=>void read()}>خواندن پیش‌نویس از PostgreSQL</button>
     <button disabled={busy||!!pending||!!draft||!refOk(item)||!refOk(source)}
      onClick={()=>void create()}>ایجاد پیش‌نویس آزمایشی</button></div>
    <p role="status">{status}</p>
    {draft&&<div data-testid="org-draft-source">شناسه: <code>{draft.itemRef}</code> | منبع: <code>{draft.sourceRef}</code></div>}
   </section>
   <section className="panel"><h2>فعالیت نسخه‌دار فنی</h2>
    <label>ارجاع یادداشت فنی آزمایشی<input aria-label="ارجاع یادداشت فنی آزمایشی"
     dir="ltr" value={note} disabled={busy||!!pending} onChange={e=>setNote(e.target.value)}/></label>
    <div className="actions"><button disabled={!history||busy||!!pending||!refOk(note)}
     onClick={append}>ثبت فعالیت نسخه‌دار</button>
     <button disabled={!draft||busy||!!pending} onClick={()=>void loadHistory()}>
      بازخوانی تاریخچه فعالیت</button></div>
    {pending&&<section role="alert" data-testid="org-draft-uncertain">
     <p>نتیجه ثبت نامعلوم است. تنها همان payload و requestRef بازپخش می‌شود.</p>
     <code>{pending.requestRef}</code>
     <button disabled={busy} onClick={()=>void inspectPending()}>
       بررسی فقط‌خواندنی ثبت قبلی</button>
     <button disabled={busy} onClick={()=>void send(pending)}>بازپخش دقیق درخواست قبلی</button>
    </section>}
    {history&&<div data-testid="org-activity-history">
     <p>نسخه جاری فعالیت: {history.currentNo}</p>
     <ol>{history.events.map(e=><li key={e.activityNo}>نسخه {e.activityNo} | <code>{e.noteRef}</code></li>)}</ol>
    </div>}
   </section>
   {history&&<OrgDraftTimeline key={item+':'+history.currentNo}
    itemRef={item} currentNo={history.currentNo} locked={busy||!!pending}/>}
   <OrgDraftQueue locked={busy||!!pending} onSelect={chosen=>void read(chosen)}/>
   <section className="panel"><h2>فهرست محدود پیش‌نویس‌ها</h2>
    <label>شروع فهرست پس از شناسه<input aria-label="شروع فهرست پس از شناسه"
     dir="ltr" value={cursor} disabled={busy||!!pending}
     onChange={e=>setCursor(e.target.value)}/></label>
    <button disabled={busy||!!pending||(cursor!==''&&!refOk(cursor))}
     onClick={()=>void list(cursor)}>خواندن فهرست PostgreSQL</button>
    <p role="status">{listStatus}</p>
    {items&&<div data-testid="org-draft-list"><ul>{items.map(d=><li key={d.itemRef}>
     <code>{d.itemRef}</code><button disabled={busy||!!pending} onClick={()=>select(d.itemRef)}>
      انتخاب پیش‌نویس</button></li>)}</ul>
     {next&&<button disabled={busy||!!pending} onClick={()=>void list(next)}>صفحه بعد</button>}
    </div>}
   </section>
  </main>
 </div>;
}
