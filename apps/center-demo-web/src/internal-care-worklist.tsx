import {useRef,useState} from 'react';

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
 const [phase,setPhase]=useState<'IDLE'|'LOADING'|'READY'|'DENIED'>('IDLE');
 const epoch=useRef(0);
 function invalidate(){
  epoch.current++;setRows([]);setNext(null);setPhase('IDLE');
 }
 function changeCursor(value:string){invalidate();setCursor(value);}
 function changeFilter(value:Filter){invalidate();setFilter(value);}
 async function page(after:string){
  if(disabled||phase==='LOADING'||(after.length>0&&!testRef(after)))return;
  const current=++epoch.current;
  const selected=filter;
  setRows([]);setNext(null);setPhase('LOADING');
  try{
   const response=await fetch('/internal/demo/v1/care-coordination-worklist?afterCaseRef='+
     encodeURIComponent(after)+'&limit='+LIMIT+'&claimFilter='+selected,
     {credentials:'omit',cache:'no-store'});
   if(!response.ok)throw Error('GET_DENIED');
   const raw:unknown=await response.json();
   if(!verified(raw,after,selected))throw Error('UNVERIFIED');
   if(epoch.current!==current)return;
   setRows(raw.cases);setNext(raw.nextAfterCaseRef);setPhase('READY');
  }catch{
   if(epoch.current!==current)return;
   setRows([]);setNext(null);setPhase('DENIED');
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
  <p role="status">{phase==='IDLE'?'هنوز فهرستی خوانده نشده است.':
    phase==='LOADING'?'در حال خواندن فهرست از PostgreSQL':
    phase==='DENIED'?'فهرست قابل تأیید نیست؛ هیچ ردیفی قابل انتخاب نیست.':
    rows.length===0?'در این صفحه درخواست ثبت‌شده‌ای نیست.':
    'فهرست ممیزی‌شده PostgreSQL خوانده شد.'}</p>
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
