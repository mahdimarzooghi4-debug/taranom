import {useRef,useState} from 'react';
import {valid as validJournal} from './care-journal-source';

// TRN-TECH-186: synthetic nonclinical coordination case locator only.
// Not a member directory or source of coordinator authorization.
type Row={caseRef:string;revision:number;contactClaims:number;sourceClaims:number};
type Page={contract:'TRN-TECH-186';syntheticOnly:true;unverifiedClaimsOnly:true;
 source:'VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL';afterCaseRef:string;
 claimFilter:'ALL'|'HAS_CONTACT'|'HAS_SOURCE'|'HAS_BOTH';
 limit:number;hasMore:boolean;nextAfterCaseRef:string|null;cases:Row[];
 realMemberAuthenticated:false;realCoordinatorVerified:false;
 clinicalSourceVerified:false;clinicianAuthorized:false;
 referralOrdered:false;referralDelivered:false;appointmentBooked:false;
 treatmentStarted:false;medicalRecordAccessGranted:false;
 emergencyServiceDispatched:false;consentGranted:false;
 walletMutated:false;nationalReleaseAllowed:false};
const deny=['realMemberAuthenticated','realCoordinatorVerified',
 'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
 'referralDelivered','appointmentBooked','treatmentStarted',
 'medicalRecordAccessGranted','emergencyServiceDispatched',
 'consentGranted','walletMutated','nationalReleaseAllowed'] as const;
const testRef=(x:string)=>x.length<=128&&/^test:[A-Za-z0-9_:-]{1,123}$/.test(x);
const LIMIT=4;
type Filter=Page['claimFilter'];
type Activity={caseRef:string;newContactClaims:number;newSourceClaims:number;revision:number};
type Audited={caseRef:string;revision:number;contactClaims:number;sourceClaims:number};
// Every row is independently reread from the same restricted, audited care
// journal. A mismatch invalidates the entire visible page, not just that row.
async function auditCase(row:Row):Promise<Audited>{
 const response=await fetch('/internal/demo/v1/care-coordination/'+
   encodeURIComponent(row.caseRef),{credentials:'omit',cache:'no-store'});
 if(!response.ok)throw Error('CARE_SOURCE_UNAVAILABLE');
 const raw:unknown=await response.json();
 if(!validJournal(raw,row.caseRef)||raw.outcome!=='READ'||
   raw.revision!==row.revision)throw Error('CARE_SOURCE_CONFLICT');
 const contact=raw.events.filter(e=>e.action==='RecordUnverifiedCoordinatorContact').length;
 const source=raw.events.filter(e=>e.action==='AttachUnverifiedClinicalSourceReference').length;
 if(contact!==row.contactClaims||source!==row.sourceClaims)
   throw Error('CARE_SOURCE_COUNT_CONFLICT');
 return {caseRef:row.caseRef,revision:raw.revision,
  contactClaims:contact,sourceClaims:source};
}
// A same-page comparison shows additions ONLY to already source-verified
// immutable-claim counts. It is not a case notification or care assignment.
function additions(before:Row[],after:Row[],beforeNext:string|null,
 afterNext:string|null):Activity[]|null{
 if(before.length!==after.length||beforeNext!==afterNext)return null;
 const changed:Activity[]=[];
 for(let i=0;i<before.length;i++){
  const old=before[i],fresh=after[i];
  if(old.caseRef!==fresh.caseRef||fresh.revision<old.revision||
   fresh.contactClaims<old.contactClaims||fresh.sourceClaims<old.sourceClaims||
   fresh.revision-old.revision!==
   fresh.contactClaims-old.contactClaims+fresh.sourceClaims-old.sourceClaims)
   return null;
  if(fresh.revision>old.revision)changed.push({caseRef:fresh.caseRef,
   newContactClaims:fresh.contactClaims-old.contactClaims,
   newSourceClaims:fresh.sourceClaims-old.sourceClaims,revision:fresh.revision});
 }
 return changed;
}
function verified(input:unknown,after:string,filter:Filter):input is Page{
 if(!input||typeof input!=='object'||Array.isArray(input))return false;
 const p=input as Partial<Page>;
 if(p.contract!=='TRN-TECH-186'||p.syntheticOnly!==true||
    p.unverifiedClaimsOnly!==true||
    p.source!=='VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL'||
    p.afterCaseRef!==after||p.claimFilter!==filter||p.limit!==LIMIT||
    !deny.every(key=>p[key]===false)||
    typeof p.hasMore!=='boolean'||!Array.isArray(p.cases)||
    p.cases.length>LIMIT)return false;
 let previous=after;
 for(const x of p.cases){
  if(!x||typeof x.caseRef!=='string'||!testRef(x.caseRef)||
    x.caseRef<=previous||!Number.isSafeInteger(x.revision)||
    x.revision<1||!Number.isSafeInteger(x.contactClaims)||
    !Number.isSafeInteger(x.sourceClaims)||x.contactClaims<0||
    x.sourceClaims<0||x.contactClaims+x.sourceClaims!==x.revision-1||
    (filter==='HAS_CONTACT'&&x.contactClaims===0)||
    (filter==='HAS_SOURCE'&&x.sourceClaims===0)||
    (filter==='HAS_BOTH'&&(x.contactClaims===0||x.sourceClaims===0)))
    return false;
  previous=x.caseRef;
 }
 return p.hasMore?
   p.cases.length===LIMIT&&p.nextAfterCaseRef===previous:
   p.nextAfterCaseRef===null;
}
export function InternalCareWorklist({disabled,onSelect}:{
 disabled:boolean;onSelect:(ref:string)=>void
}){
 const [cursor,setCursor]=useState('');
 const [filter,setFilter]=useState<Filter>('ALL');
 const [rows,setRows]=useState<Row[]>([]);
 const [next,setNext]=useState<string|null>(null);
 const [pageAfter,setPageAfter]=useState('');
 const [activity,setActivity]=useState<Activity[]|null>(null);
 const [audit,setAudit]=useState<Audited[]|null>(null);
 const [phase,setPhase]=useState<'IDLE'|'LOADING'|'READY'|'DENIED'>('IDLE');
 const epoch=useRef(0);
 function invalidate(){
  epoch.current++;setRows([]);setNext(null);setActivity(null);setAudit(null);
  setPageAfter('');setPhase('IDLE');
 }
 function changeCursor(value:string){invalidate();setCursor(value);}
 function changeFilter(value:Filter){invalidate();setFilter(value);}
 async function page(after:string,check=false){
  if(disabled||phase==='LOADING'||(after.length>0&&!testRef(after))||
    (check&&(phase!=='READY'||rows.length===0||pageAfter!==after)))return;
  const previous=check?rows:null,previousNext=next;
  const current=++epoch.current;
  const selected=filter;
  setRows([]);setNext(null);setActivity(null);setAudit(null);setPhase('LOADING');
  try{
   const response=await fetch('/internal/demo/v1/care-coordination-worklist?afterCaseRef='+
     encodeURIComponent(after)+'&limit='+LIMIT+'&claimFilter='+selected,
     {credentials:'omit',cache:'no-store'});
   if(!response.ok)throw Error('GET_DENIED');
   const raw:unknown=await response.json();
   if(!verified(raw,after,selected))throw Error('UNVERIFIED');
   const delta=previous?additions(previous,raw.cases,previousNext,
    raw.nextAfterCaseRef):null;
   if(previous&&delta===null)throw Error('PAGE_MEMBERSHIP_OR_HISTORY_CHANGED');
   if(epoch.current!==current)return;
   setRows(raw.cases);setNext(raw.nextAfterCaseRef);setPageAfter(after);
   setActivity(delta);setPhase('READY');
  }catch{
   if(epoch.current!==current)return;
   setRows([]);setNext(null);setActivity(null);setAudit(null);setPageAfter('');
   setPhase('DENIED');
  }
 }
 async function inspectPage(){
  if(disabled||phase!=='READY'||rows.length===0)return;
  const pinned=rows.map(row=>({...row})),pinnedNext=next,pinnedAfter=pageAfter;
  const current=++epoch.current;
  setRows([]);setNext(null);setActivity(null);setAudit(null);setPhase('LOADING');
  try{
   const checked:Audited[]=[];
   // Small technical four-row limit; never re-write, silently skip, or
   // accept only some rows. A concurrent POST produces a version conflict.
   for(const row of pinned)checked.push(await auditCase(row));
   if(epoch.current!==current)return;
   setRows(pinned);setNext(pinnedNext);setPageAfter(pinnedAfter);
   setAudit(checked);setPhase('READY');
  }catch{
   if(epoch.current!==current)return;
   setRows([]);setNext(null);setActivity(null);setAudit(null);
   setPageAfter('');setPhase('DENIED');
  }
 }
 return <section className="panel" data-testid="center-care-worklist">
  <h3>فهرست درخواست‌های هماهنگی غیرتشخیصی — صرفاً ساختگی</h3>
  <p>تنها شناسه پرونده و شمارش ادعاهای ثبت‌شده از ژورنال ممیزی‌شده.
    هیچ اولویت، تشخیص، وضعیت انجام خدمت، احراز هویت یا ارجاعی اعلام نمی‌شود.</p>
  <label>نوع ادعای ثبت‌شده در ژورنال — بدون اولویت یا قضاوت
   <select aria-label="فیلتر ادعاهای هماهنگی" value={filter}
    disabled={disabled||phase==='LOADING'}
    onChange={e=>changeFilter(e.target.value as Filter)}>
    <option value="ALL">همه درخواست‌های ثبت‌شده</option>
    <option value="HAS_CONTACT">دارای ادعای تماس</option>
    <option value="HAS_SOURCE">دارای مرجع بالینی ادعایی</option>
    <option value="HAS_BOTH">دارای هر دو نوع ادعا</option>
   </select>
  </label>
  <label>شروع پس از شناسه پرونده آزمایشی (اختیاری)
   <input aria-label="شروع فهرست هماهنگی پس از پرونده" value={cursor}
    maxLength={128} disabled={disabled||phase==='LOADING'}
    onChange={e=>changeCursor(e.target.value)}/>
  </label>
  <button type="button" disabled={disabled||phase==='LOADING'||
    (cursor.length>0&&!testRef(cursor))}
    onClick={()=>void page(cursor)}>خواندن فهرست هماهنگی از ژورنال</button>
  {next&&<button type="button" disabled={disabled||phase==='LOADING'}
    onClick={()=>void page(next)}>صفحه بعد هماهنگی‌ها</button>}
  {phase==='READY'&&rows.length>0&&<>
   <button type="button" disabled={disabled}
    onClick={()=>void page(pageAfter,true)}>بررسی دستی تغییرات پرونده‌های همین صفحه</button>
   <button type="button" disabled={disabled}
    onClick={()=>void inspectPage()}>ممیزی مستقل همه پرونده‌های همین صفحه</button>
  </>}
  <p role="status">{phase==='IDLE'?'هنوز فهرستی خوانده نشده است.':
    phase==='LOADING'?'در حال خواندن فهرست از PostgreSQL':
    phase==='DENIED'?'فهرست قابل تأیید نیست؛ هیچ ردیفی قابل انتخاب نیست.':
    rows.length===0?'در این صفحه درخواست ثبت‌شده‌ای نیست.':
    'فهرست ممیزی‌شده PostgreSQL خوانده شد.'}</p>
  {phase==='READY'&&activity!==null&&
   <section data-testid="center-care-worklist-activity" role="region"
    aria-label="تغییرات ادعایی صفحه فعلی">
    <p>از آخرین خواندن همین صفحه، {activity.length} پرونده با ادعاهای تازه مشاهده شد.
     این گزارش اعلان رسمی، اولویت درمانی یا تأیید انجام خدمت نیست.</p>
    <ul>{activity.map(a=><li key={a.caseRef}>
     پرونده <code>{a.caseRef}</code> — نسخه {a.revision}،
     ادعای تماس جدید {a.newContactClaims}،
     مرجع بالینی تأییدنشده جدید {a.newSourceClaims}
    </li>)}</ul>
   </section>}
  {phase==='READY'&&audit!==null&&
   <section data-testid="center-care-worklist-audit" role="region"
    aria-label="ممیزی مستقل صفحه">
    <p>{audit.length} پرونده به‌صورت مستقل از ژورنال PostgreSQL آزمایشی
     با نسخه و شمارش ادعاهای همین صفحه تطبیق داده شدند؛
     این اعتبارسنجی مجوز درمان، تأیید منبع بالینی یا احراز هویت نیست.</p>
    <ul>{audit.map(a=><li key={a.caseRef}>
     پرونده <code>{a.caseRef}</code> — نسخه {a.revision}،
     ادعای تماس {a.contactClaims}،
     مرجع بالینی تأییدنشده {a.sourceClaims}
    </li>)}</ul>
   </section>}
  {phase==='READY'&&<ul data-testid="center-care-worklist-rows">
   {rows.map(r=><li key={r.caseRef}>پرونده <code>{r.caseRef}</code>
    {' — نسخه '+r.revision+'، ادعاهای تماس '+r.contactClaims+
      '، ارجاع‌های تأییدنشده '+r.sourceClaims}
    <button type="button" disabled={disabled}
      onClick={()=>onSelect(r.caseRef)}>انتخاب پرونده هماهنگی</button>
   </li>)}
  </ul>}
  <small>صرفاً آزمایش محلی؛ هیچ دسترسی به داده یا خدمات واقعی عضو ایجاد نمی‌شود.</small>
 </section>;
}
