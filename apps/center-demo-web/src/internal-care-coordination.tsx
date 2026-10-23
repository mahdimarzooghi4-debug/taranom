import {useRef,useState} from 'react';
import {InternalCareWorklist} from './internal-care-worklist';
import {careReceipt,parseCareReceipt} from './care-command-recovery';
import {type Event,type Snapshot,valid,testRef,uuid} from './care-journal-source';
import {type CareHistoryPage,type HistoryFilter,CARE_HISTORY_LIMIT,validHistoryPage} from './care-pinned-history';

type FollowUpAction='RecordUnverifiedCoordinatorContact'|
 'AttachUnverifiedClinicalSourceReference';
type Original={ref:string;body:string;requestId:string;action:FollowUpAction;
 expectedRevision:number;memberRef:string;originRef:string;
 originalId:string;actor:string;note:string};
// Compare only the original immutable journal event fields after an
// independent, validated PostgreSQL READ. This is not a delivery receipt.
const equalEvent=(a:Event,b:Event):boolean=>
 a.revision===b.revision&&a.action===b.action&&a.requestId===b.requestId&&
 a.memberRef===b.memberRef&&a.originalMemberRequestRef===b.originalMemberRequestRef&&
 a.originalSubmissionRequestId===b.originalSubmissionRequestId&&
 a.claimedActorRef===b.claimedActorRef&&
 a.unverifiedNoteOrDocumentRef===b.unverifiedNoteOrDocumentRef;
const appended=(prior:Snapshot,next:Snapshot):Event[]|null=>
 prior.caseRef===next.caseRef&&next.revision>=prior.revision&&
 prior.events.every((e,i)=>equalEvent(e,next.events[i]))
 ?next.events.slice(prior.revision):null;
function acknowledged(source:Snapshot,original:Original){
 if(source.revision<original.expectedRevision+1)return false;
 const matches=source.events.filter(e=>e.requestId===original.requestId);
 return matches.length===1&&
 matches[0].revision===original.expectedRevision+1&&
 matches[0].action===original.action&&
 matches[0].memberRef===original.memberRef&&
 matches[0].originalMemberRequestRef===original.originRef&&
 matches[0].originalSubmissionRequestId===original.originalId&&
 matches[0].claimedActorRef===original.actor&&
 matches[0].unverifiedNoteOrDocumentRef===original.note;
}
export function InternalCareCoordination(){
 const [ref,setRef]=useState('test:care-center-qa');
 const [actor,setActor]=useState('test:claimed-coordinator');
 const [note,setNote]=useState('test:claimed-contact-note');
 const [sourceRef,setSourceRef]=useState('test:unverified-clinical-source');
 const [snapshot,setSnapshot]=useState<Snapshot|null>(null);
 const [historyPage,setHistoryPage]=useState<CareHistoryPage|null>(null);
 const [historyFilter,setHistoryFilter]=useState<HistoryFilter>('ALL');
 const [activity,setActivity]=useState<Event[]|null>(null);
 const [pending,setPending]=useState<Original|null>(null);
 const [recoveryText,setRecoveryText]=useState('');
 const [busy,setBusy]=useState(false);
 const [status,setStatus]=useState('ابتدا درخواست آزمایشی عضو را از ژورنال بخوانید.');
 const epoch=useRef(0);
 const url=(caseRef:string)=>'/internal/demo/v1/care-coordination/'+encodeURIComponent(caseRef);
 function changeRef(value:string){
  epoch.current++;setRef(value);setSnapshot(null);setActivity(null);setHistoryPage(null);
  setStatus('شناسه پرونده تغییر کرد؛ منبع قبلی قابل استفاده نیست.');
 }
 async function sourceRead(caseRef:string){
  const response=await fetch(url(caseRef),{cache:'no-store',credentials:'omit'});
  if(!response.ok)throw Error('READ_UNAVAILABLE');
  const raw:unknown=await response.json();
  if(!valid(raw,caseRef)||raw.outcome!=='READ')throw Error('INVALID_SOURCE');
  return raw;
 }
 async function read(){
  if(busy||pending||!testRef(ref))return;
  const previous=snapshot;
  const generation=++epoch.current;setBusy(true);setSnapshot(null);setActivity(null);setHistoryPage(null);
  try{
   const fresh=await sourceRead(ref);
   if(epoch.current!==generation)return;
   if(fresh.revision===0)throw Error('ORIGINAL_MISSING');
   const delta=previous&&previous.caseRef===ref?appended(previous,fresh):null;
   if(previous&&previous.caseRef===ref&&delta===null)
    throw Error('HISTORICAL_JOURNAL_CHANGED_OR_STALE');
   setSnapshot(fresh);setActivity(delta);
   setStatus(delta===null?'درخواست و ادعاهای قبلی از PostgreSQL خوانده شد؛ هیچ هماهنگ‌کننده واقعی احراز نشده است.':
    'رویدادهای تازه مرکز فقط با خواندن مستقل PostgreSQL بررسی شدند؛ هیچ خدمت واقعی احراز نشده است.');
  }catch{
   if(epoch.current===generation)setStatus('درخواست اولیه معتبر پیدا نشد یا منبع نامعتبر است.');
  }finally{setBusy(false);}
 }
 async function readHistory(after:number){
  if(busy||pending||!snapshot||!Number.isSafeInteger(after)||
    after<0||after>snapshot.revision)return;
  const pinned=snapshot,selected=historyFilter,generation=++epoch.current;
  setBusy(true);setHistoryPage(null);
  try{
   const response=await fetch(url(pinned.caseRef)+'/history?afterRevision='+
    after+'&limit='+CARE_HISTORY_LIMIT+
    '&expectedCurrentRevision='+pinned.revision+'&claimType='+selected,
    {cache:'no-store',credentials:'omit'});
   if(!response.ok)throw Error('PINNED_HISTORY_READ_DENIED');
   const page:unknown=await response.json();
   if(!validHistoryPage(page,pinned,after,selected))
    throw Error('PINNED_HISTORY_UNVERIFIABLE');
   if(epoch.current!==generation)return;
   setHistoryPage(page);
   setStatus('صفحه تاریخچه مرکز با نسخه ثابت از ژورنال PostgreSQL تأیید شد؛ هیچ اقدام واقعی ایجاد نشد.');
  }catch{
   if(epoch.current===generation){
    setSnapshot(null);setActivity(null);setHistoryPage(null);
    setStatus('تاریخچه نسخه‌ثابت قابل تأیید نیست؛ داده و فرمان‌های قدیمی کنار گذاشته شدند. پرونده را مستقل دوباره بخوانید.');
   }
  }finally{setBusy(false);}
 }
 async function verifiedPending(){
  if(busy||!pending)return;
  const original=pending,generation=++epoch.current;
  setBusy(true);setSnapshot(null);setActivity(null);setHistoryPage(null);
  try{
   const fresh=await sourceRead(original.ref);
   if(!acknowledged(fresh,original))throw Error('ORIGINAL_NOT_COMMITTED');
   if(epoch.current!==generation)return;
   setPending(null);setSnapshot(fresh);
   setStatus(original.action==='RecordUnverifiedCoordinatorContact'?
    'دقیقاً همان ادعای تماس بدون POST تازه از PostgreSQL بازیابی شد.':
    'دقیقاً همان مرجع بالینی تأییدنشده بدون POST تازه از PostgreSQL بازیابی شد؛ هیچ مجوزی ایجاد نشده است.');
  }catch{
   if(epoch.current===generation)setStatus('رویداد دقیق تأیید نشد؛ همان UUID برای بازپخش دستی محفوظ است.');
  }finally{setBusy(false);}
 }
 async function send(original:Original){
  if(busy)return;
  const generation=++epoch.current;setBusy(true);setSnapshot(null);setActivity(null);setHistoryPage(null);
  try{
   const response=await fetch(url(original.ref)+'/commands',{
    method:'POST',headers:{'Content-Type':'application/json'},
    credentials:'omit',cache:'no-store',body:original.body});
   const raw:unknown=await response.json();
   if(!response.ok||!valid(raw,original.ref)||
      !['RecordedUnverified','Replayed'].includes(raw.outcome)||
      !acknowledged(raw,original))throw Error('UNCERTAIN_ACK');
   // A successful HTTP reply alone is not proof of this exact command.
   const source=await sourceRead(original.ref);
   if(!acknowledged(source,original))throw Error('SOURCE_MISMATCH');
   if(epoch.current!==generation)return;
   setPending(null);setSnapshot(source);
   setStatus(original.action==='RecordUnverifiedCoordinatorContact'?
    'ادعای تماس دقیقاً در ژورنال ثبت و جداگانه تأیید شد؛ تماس واقعی احراز نشده است.':
    'مرجع بالینی تأییدنشده دقیقاً ثبت و جداگانه تأیید شد؛ هیچ اعتبار بالینی یا مجوز دسترسی صادر نشده است.');
  }catch{
   if(epoch.current===generation){
    setPending(original);setStatus('نتیجه ثبت نامعلوم است؛ فرمان جدید قفل شد.');
   }
  }finally{setBusy(false);}
 }
 function loadRecovery(){
  if(busy||pending)return;
  const imported=parseCareReceipt(recoveryText,'CENTER');
  if(!imported){
   setStatus('رسید بازیابی هماهنگی مرکز نامعتبر است؛ هیچ فرمانی ارسال نشد.');
   return;
  }
  const c=imported.command;
  // Parser excludes initial-member request for this surface.
  const action:FollowUpAction=c.action==='RECORD_UNVERIFIED_COORDINATOR_CONTACT'
   ?'RecordUnverifiedCoordinatorContact':'AttachUnverifiedClinicalSourceReference';
  epoch.current++;
  setRef(imported.caseRef);
  setSnapshot(null);setActivity(null);setHistoryPage(null);
  setPending({ref:imported.caseRef,body:imported.body,
   requestId:c.requestId,action,expectedRevision:c.expectedRevision,
   memberRef:c.memberRef,originRef:c.originalMemberRequestRef,
   originalId:c.originalSubmissionRequestId,
   actor:c.claimedActorRef!,note:c.unverifiedNoteOrDocumentRef!});
  setStatus('رسید معتبر آزمایشی بارگذاری شد؛ هیچ POST یا GET اجرا نشد. فرمان تازه قفل است.');
 }
 function submit(action:FollowUpAction){
  // Separate human-selected domain actions with separately entered refs.
  // Neither creates verification, clinical approval or patient access.
  const documentRef=action==='RecordUnverifiedCoordinatorContact'?note:sourceRef;
  if(!snapshot||snapshot.caseRef!==ref||pending||busy||
    !testRef(ref)||!testRef(actor)||!testRef(documentRef))return;
  const root=snapshot.events[0];
  if(!root||!uuid(root.requestId))return;
  const requestId=crypto.randomUUID();
  const body=JSON.stringify({
   requestId,expectedRevision:snapshot.revision,
   action:action==='RecordUnverifiedCoordinatorContact'?
    'RECORD_UNVERIFIED_COORDINATOR_CONTACT':'ATTACH_UNVERIFIED_CLINICAL_SOURCE',
   memberRef:root.memberRef,
   originalMemberRequestRef:root.originalMemberRequestRef,
   originalSubmissionRequestId:root.requestId,
   claimedActorRef:actor,unverifiedNoteOrDocumentRef:documentRef
  });
  const original:Original={ref,body,requestId,action,
   expectedRevision:snapshot.revision,memberRef:root.memberRef,
   originRef:root.originalMemberRequestRef,
   originalId:root.requestId,actor,note:documentRef};
  setPending(original);
  void send(original);
 }
 return <section className="panel" data-testid="center-care-coordination">
  <h2>پیگیری دستی هماهنگی غیرتشخیصی — فقط آزمایش</h2>
  <p>این محیط، پرونده درمانی یا حساب عضو نیست. فقط رویدادهای ادعایی
    ثبت‌شده در ژورنال PostgreSQL عضو را با شناسه test: مشاهده می‌کند.
    مجوز دسترسی، ارجاع و تأیید تماس واقعی وجود ندارد.</p>
  <InternalCareWorklist disabled={busy||!!pending} onSelect={changeRef}/>
  <label>شناسه هماهنگی آزمایشی
   <input aria-label="شناسه پرونده هماهنگی مرکز" value={ref} maxLength={100}
    disabled={busy||!!pending} onChange={e=>changeRef(e.target.value)}/>
  </label>
  <button type="button" disabled={busy||!!pending||!testRef(ref)}
    onClick={()=>void read()}>خواندن مستقل هماهنگی عضو</button>
  {snapshot&&<button type="button" disabled={busy||!!pending}
   onClick={()=>void read()}>بررسی دستی تغییرات هماهنگی مرکز</button>}
  <p data-testid="center-care-status" role="status">{status}</p>
  {activity!==null&&snapshot&&<section data-testid="center-care-activity" role="status">
   <p>پس از خواندن قبلی همین صفحه {activity.length} ادعای جدید اضافه شده است.
    این تأیید تماس، دسترسی پزشکی یا اعلان رسمی نیست.</p>
   <ul>{activity.map(e=><li key={e.requestId}>
    نسخه {e.revision}: {e.action} — {e.unverifiedNoteOrDocumentRef??'بدون مرجع'}
   </li>)}</ul>
  </section>}
  {pending&&<section data-testid="center-care-uncertain" role="alert">
   <p>نتیجه فرمان قبلی نامعلوم است؛ هیچ فرمان تازه‌ای صادر نمی‌شود.</p>
   <p>عملیات قفل‌شده: {pending.action==='RecordUnverifiedCoordinatorContact'?
    'ادعای تماس':'مرجع بالینی تأییدنشده'} — UUID {pending.requestId}</p>
   <label>رسید متنیِ دستی هماهنگی مرکز — فقط آزمایش، بدون مجوز
    <textarea data-testid="center-care-recovery-export" readOnly rows={4}
     value={careReceipt('CENTER',pending.ref,pending.body)}/></label>
   <p>پیش از بستن صفحه، متن را به‌صورت دستی نگه دارید؛ در مرورگر ذخیره نمی‌شود.</p>
   <button type="button" disabled={busy} onClick={()=>void verifiedPending()}>
    {pending.action==='RecordUnverifiedCoordinatorContact'?
     'بررسی فقط‌خواندنی همان تماس':'بررسی فقط‌خواندنی همان مرجع'}
   </button>
   <button type="button" disabled={busy} onClick={()=>void send(pending)}>
    بازپخش دستی دقیق UUID قبلی
   </button>
  </section>}
  <fieldset aria-label="بازیابی دستی هماهنگی مرکز">
   <legend>بازیابی دستی فرمان نامطمئن پس از بارگذاری مجدد</legend>
   <label>متن رسید بازیابی هماهنگی مرکز
    <textarea value={recoveryText} maxLength={4096} rows={4}
     disabled={busy||!!pending} onChange={e=>setRecoveryText(e.target.value)}/></label>
   <button type="button" disabled={busy||!!pending}
    onClick={loadRecovery}>بارگذاری رسید هماهنگی مرکز بدون ارسال</button>
   <p>بارگذاری رسید نه منبع بالینی را تأیید می‌کند، نه مجوزی به مرکز می‌دهد.</p>
  </fieldset>
  {snapshot&&<section data-testid="center-care-server-history" role="region"
    aria-label="تاریخچه نسخه‌ثابت هماهنگی مرکز">
   <h3>تاریخچه صفحه‌بندی‌شده هماهنگی مرکز، فقط خواندنی</h3>
   <p>هر صفحه جداگانه با نسخه {snapshot.revision} از PostgreSQL آزمایشی
    خوانده می‌شود. این سند پزشکی، تأیید تماس، رضایت یا اعلان واقعی نیست.</p>
   <label>نوع ادعای تاریخچه نسخه‌ثابت مرکز
    <select aria-label="فیلتر تاریخچه هماهنگی مرکز" value={historyFilter}
     disabled={busy||!!pending}
     onChange={e=>{setHistoryFilter(e.target.value as HistoryFilter);setHistoryPage(null);}}>
     <option value="ALL">تمام رویدادها</option>
     <option value="CONTACT">فقط ادعاهای تماس</option>
     <option value="SOURCE">فقط ارجاع‌های منبع بالینی تأییدنشده</option>
    </select>
   </label>
   <button type="button" disabled={busy||!!pending}
    onClick={()=>void readHistory(0)}>خواندن نخستین صفحه تاریخچه مرکز</button>
   {historyPage&&<section data-testid="center-care-server-history-page">
    <p>نسخه پین‌شده: {historyPage.expectedCurrentRevision} |
     شروع پس از نسخه: {historyPage.afterRevision} |
     نوع ادعا: {historyPage.claimType}</p>
    <ol>{historyPage.events.map(e=><li key={e.requestId}>
     نسخه {e.revision}: {e.action} |
     UUID <code>{e.requestId}</code> |
     عضو ادعایی <code>{e.memberRef}</code> |
     ارجاع اولیه <code>{e.originalMemberRequestRef}</code> |
     شناسه درخواست اصلی <code>{e.originalSubmissionRequestId}</code> |
     مدعی هماهنگ‌کننده <code>{e.claimedActorRef??'ثبت نشده'}</code> |
     مرجع ادعایی <code>{e.unverifiedNoteOrDocumentRef??'ثبت نشده'}</code>
    </li>)}</ol>
   </section>}
   {historyPage?.hasMore&&historyPage.nextAfterRevision!==null&&
    <button type="button" disabled={busy||!!pending}
     onClick={()=>void readHistory(historyPage!.nextAfterRevision!)}>
     صفحه بعدی تاریخچه مرکز با همان نسخه
    </button>}
   <p>این مشاهده هیچ رویداد یا اختیار جدیدی به پرونده اضافه نمی‌کند.</p>
  </section>}
  {snapshot&&<section data-testid="center-care-source">
   <p>پرونده: <code>{snapshot.caseRef}</code> — نسخه {snapshot.revision}</p>
   <p>عضو ادعایی: <code>{snapshot.events[0].memberRef}</code>
    | درخواست اولیه: <code>{snapshot.events[0].originalMemberRequestRef}</code></p>
   <ul>{snapshot.events.map(e=><li key={e.requestId}>
    نسخه {e.revision}: {e.action}
    {e.unverifiedNoteOrDocumentRef&&
     <> — ارجاع تأییدنشده <code>{e.unverifiedNoteOrDocumentRef}</code></>}
   </li>)}</ul>
   <label>مرجع هماهنگ‌کننده ادعایی
    <input aria-label="مرجع هماهنگ‌کننده آزمایشی" value={actor}
     maxLength={100} disabled={busy||!!pending}
     onChange={e=>setActor(e.target.value)}/>
   </label>
   <label>مرجع یادداشت تماس ادعایی
    <input aria-label="مرجع یادداشت تماس آزمایشی" value={note}
     maxLength={100} disabled={busy||!!pending}
     onChange={e=>setNote(e.target.value)}/>
   </label>
   <button type="button" disabled={busy||!!pending||!testRef(actor)||!testRef(note)}
    onClick={()=>submit('RecordUnverifiedCoordinatorContact')}>ثبت مستقل ادعای تماس هماهنگ‌کننده</button>
   <label>مرجع منبع بالینی ادعایی، بدون احراز صحت
    <input aria-label="مرجع بالینی تأییدنشده آزمایشی" value={sourceRef}
     maxLength={100} disabled={busy||!!pending}
     onChange={e=>setSourceRef(e.target.value)}/>
   </label>
   <p>ثبت این مرجع صرفاً وجود یک ادعای منبع را نگه می‌دارد؛
    اصل پرونده، اجازه درمان، صحت مدرک یا رضایت عضو بررسی نمی‌شود.</p>
   <button type="button" disabled={busy||!!pending||!testRef(actor)||!testRef(sourceRef)}
    onClick={()=>submit('AttachUnverifiedClinicalSourceReference')}>
    ثبت مستقل مرجع بالینی تأییدنشده
   </button>
   <p>احراز هویت و صلاحیت: خیر | تماس و ارجاع واقعی: خیر |
    درمان و نوبت: خیر | انتشار ملی: ممنوع</p>
  </section>}
 </section>;
}
