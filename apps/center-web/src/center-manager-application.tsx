import {useEffect,useRef,useState,type FormEvent} from 'react';
import {TaranomApiError,getMyManagerClaims,submitManagerClaim,recoverManagerClaim,
 type MyManagerClaim,type ManagerClaimState} from './taranom-api';
import './center-manager-application.css';

type Pending={requestId:string;centerRef:string;reason:string};
const centerPattern=/^center-[a-f0-9]{32}$/;
const labels:Record<ManagerClaimState,string>={
 PENDING_REVIEW:'در انتظار بررسی ادمین',
 REVIEWED_PENDING_VERIFICATION:'بررسی‌شده؛ در انتظار احراز هویت و اختیار',
 DECLINED:'ردشده توسط ادمین'
};
export function CenterManagerApplication(){
 const [centerRef,setCenterRef]=useState(''),[reason,setReason]=useState('');
 const [requests,setRequests]=useState<MyManagerClaim[]>([]);
 const [after,setAfter]=useState<string|null>(null);
 const [nextAfter,setNextAfter]=useState<string|null>(null);
 const [refresh,setRefresh]=useState(0),[loading,setLoading]=useState(true);
 const [saving,setSaving]=useState(false),[uncertain,setUncertain]=useState(false);
 const [error,setError]=useState(''),[note,setNote]=useState('');
 const pending=useRef<Pending|null>(null),busy=useRef(false);
 useEffect(()=>{
  const controller=new AbortController();
  setLoading(true);setError('');
  getMyManagerClaims(after??undefined,controller.signal)
   .then(result=>{if(!controller.signal.aborted){
    setRequests(result.requests);setNextAfter(result.nextAfter);
   }})
   .catch(e=>{if(!controller.signal.aborted)
    setError(e instanceof Error?e.message:'فهرست درخواست‌ها دریافت نشد.')})
   .finally(()=>{if(!controller.signal.aborted)setLoading(false)});
  return()=>controller.abort();
 },[after,refresh]);
 const reload=()=>{setAfter(null);setRefresh(i=>i+1)};
 async function submit(event:FormEvent){
  event.preventDefault();
  const center=centerRef.trim(),explanation=reason.trim();
  if(busy.current||uncertain||!centerPattern.test(center)||
     explanation.length<10||explanation.length>500)return;
  const command={requestId:crypto.randomUUID(),centerRef:center,reason:explanation};
  busy.current=true;pending.current=command;setSaving(true);setError('');setNote('');
  try{
   await submitManagerClaim(command);
   pending.current=null;setCenterRef('');setReason('');
   setNote('درخواست ثبت شد و در انتظار بررسی ادمین است؛ هنوز مجوز مدیریت صادر نشده است.');
   reload();
  }catch(e){
   setError(e instanceof Error?e.message:'ثبت درخواست ناموفق بود.');
   if(e instanceof TaranomApiError&&e.status>=400&&e.status<500)
    pending.current=null;
   else setUncertain(true);
  }finally{busy.current=false;setSaving(false)}
 }
 async function reconcile(){
  const command=pending.current;if(!command||busy.current)return;
  busy.current=true;setSaving(true);setError('');
  try{
   const receipt=await recoverManagerClaim(command);
   pending.current=null;setUncertain(false);setCenterRef('');setReason('');
   setNote('نتیجه درخواست قبلی بازیابی شد: '+labels[receipt.state]+
    '؛ هیچ مجوز مدیریتی صادر نشده است.');
   reload();
  }catch(e){
   setError(e instanceof TaranomApiError&&e.status===404?
    'رسید هنوز مشاهده نشد؛ درخواست قبلی را خودکار تکرار نکنید.':
    e instanceof Error?e.message:'بازیابی رسید ناموفق بود.');
  }finally{busy.current=false;setSaving(false)}
 }
 return <main className="manager-application" dir="rtl">
  <div className="manager-application-head">
   <img src="/assets/taranom-logo.png" alt="ترنم" />
   <a href="/center-portal">بازگشت به محیط مرکز</a>
  </div>
  <h1>درخواست مدیریت مرکز درمانی</h1>
  <p>مرکز باید قبلاً توسط ادمین ترنم تعریف شده باشد. درخواست شما با هویت ورود همین حساب ثبت می‌شود؛ نوشتن یک شناسه مرکز، هیچ اختیاری ایجاد نمی‌کند.</p>
  <section className="manager-application-warning" role="note">
   <strong>درخواست و بررسی، معادل احراز اختیار نیست</strong>
   <p>ادمین ادعای شما را بررسی می‌کند. حتی پس از بررسی، انتساب رسمی و صدور مجوز مدیریت به مدارک مستقل هویت و اختیار نیاز دارد. هیچ دسترسی پزشکی، مالی یا انتشار مرکز فعال نمی‌شود.</p>
  </section>
  {error&&<p role="alert" className="manager-application-error">{error}</p>}
  {note&&<p role="status" className="manager-application-success">{note}</p>}
  <form onSubmit={e=>void submit(e)} className="manager-application-card">
   <h2>ثبت درخواست</h2>
   <label>شناسه مرجع مرکز تعریف‌شده توسط ادمین
    <input required value={centerRef} onChange={e=>setCenterRef(e.target.value)}
     pattern="center-[a-f0-9]{32}" maxLength={39} placeholder="center-..."
     dir="ltr" disabled={saving||uncertain}/>
   </label>
   <label>توضیح نسبت و اختیار ادعایی شما در مرکز
    <textarea required minLength={10} maxLength={500} rows={4}
     value={reason} onChange={e=>setReason(e.target.value)}
     disabled={saving||uncertain}/>
   </label>
   {uncertain?<button type="button" disabled={saving} onClick={()=>void reconcile()}>
    بررسی نتیجه درخواست قبلی</button>:
    <button type="submit" disabled={saving||loading||!centerPattern.test(centerRef.trim())||
     reason.trim().length<10||reason.trim().length>500}>
     {saving?'در حال ثبت…':'ارسال درخواست برای بررسی ادمین'}
    </button>}
   {uncertain&&<p>نتیجه ارسال قبلی قطعی نیست؛ ارسال مجدد بسته است و فقط رسید همان درخواست بررسی می‌شود.</p>}
  </form>
  <section className="manager-application-card">
   <div className="manager-application-row">
    <h2>درخواست‌های ثبت‌شده همین حساب</h2>
    <button type="button" disabled={saving||loading} onClick={reload}>بازخوانی</button>
   </div>
   {loading&&<p role="status">در حال دریافت درخواست‌ها…</p>}
   {!loading&&requests.length===0&&<p>درخواستی برای این حساب در این صفحه یافت نشد.</p>}
   <ul className="manager-application-history">{requests.map(r=><li key={r.requestId}>
    <bdi>{r.centerRef}</bdi><strong>{labels[r.state]}</strong>
    <small>شناسه درخواست: <bdi>{r.requestId}</bdi></small>
    {r.reviewReason&&<p>یادداشت بررسی ادمین: {r.reviewReason}</p>}
    <small>مجوز مدیریت صادر نشده است.</small>
   </li>)}</ul>
   {nextAfter&&<button type="button" disabled={loading||saving}
    onClick={()=>setAfter(nextAfter)}>صفحه بعد</button>}
  </section>
 </main>;
}
