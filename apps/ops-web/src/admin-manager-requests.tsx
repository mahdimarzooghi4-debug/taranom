import {useEffect,useRef,useState,type FormEvent} from 'react';
import './admin-manager-requests.css';

type ReviewState='PENDING_REVIEW'|'REVIEWED_PENDING_VERIFICATION'|'DECLINED';
type ManagerClaim={requestId:string;centerRef:string;applicantIssuer:string;
 applicantSubject:string;reason:string;submittedAt:string;state:ReviewState;
 reviewReason:string|null;reviewedAt:string|null;managerAssigned:false};
type Page={requests:ManagerClaim[];nextAfter:string|null;associationGateRequired:true};
type ReviewResult={outcome:'Applied'|'Replayed';requestId:string;state:ReviewState;
 managerAssigned:false};
type ReviewCommand={commandId:string;decision:Exclude<ReviewState,'PENDING_REVIEW'>;reason:string};
type PendingReview={requestId:string;command:ReviewCommand};
class ManagerAdminError extends Error{
 constructor(readonly status:number){super(status===401?'نشست ورود معتبر برای مدیریت الزامی است.':
  status===403?'مجوز بررسی درخواست مدیران مرکز را ندارید.':
  status===404?'درخواست مدیریت مرکز یافت نشد.':
  status===409?'این درخواست قبلاً بررسی شده یا شناسه عملیات تعارض دارد.':
  status===400?'دادهٔ بررسی واردشده معتبر نیست.':
  'ارتباط با سامانه بررسی درخواست‌ها ناموفق بود.')}
}
declare global{interface Window{taranomSession?:{getAccessToken:()=>Promise<string>|string}}}
const uuid=/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/;
const center=/^center-[a-f0-9]{32}$/;
const states:ReviewState[]=['PENDING_REVIEW','REVIEWED_PENDING_VERIFICATION','DECLINED'];
const labels:Record<ReviewState,string>={
 PENDING_REVIEW:'در انتظار بررسی',
 REVIEWED_PENDING_VERIFICATION:'بررسی‌شده؛ نیازمند احراز اختیار',
 DECLINED:'ردشده'
};
async function api(path:string,init:RequestInit={}):Promise<unknown>{
 const token=await window.taranomSession?.getAccessToken();
 init.signal?.throwIfAborted();
 if(!token)throw new ManagerAdminError(401);
 const res=await fetch('/api/v1/operations/center-manager-requests'+path,{
  ...init,cache:'no-store',redirect:'error',credentials:'omit',
  headers:{Accept:'application/json',Authorization:'Bearer '+token,
   ...(init.body?{'Content-Type':'application/json'}:{}),...init.headers}
 });
 if(!res.ok)throw new ManagerAdminError(res.status);
 return res.json() as Promise<unknown>;
}
function validate(raw:unknown):Page{
 const p=raw as Page;
 if(!p||p.associationGateRequired!==true||!Array.isArray(p.requests)||
  p.requests.length>25||!(p.nextAfter===null||typeof p.nextAfter==='string'&&uuid.test(p.nextAfter))||
  !p.requests.every(r=>r&&uuid.test(r.requestId)&&center.test(r.centerRef)&&
   typeof r.applicantIssuer==='string'&&typeof r.applicantSubject==='string'&&
   typeof r.reason==='string'&&states.includes(r.state)&&r.managerAssigned===false&&
   Number.isFinite(Date.parse(r.submittedAt))&&
   (r.reviewReason===null||typeof r.reviewReason==='string')&&
   (r.reviewedAt===null||typeof r.reviewedAt==='string'&&Number.isFinite(Date.parse(r.reviewedAt))))||
  new Set(p.requests.map(x=>x.requestId)).size!==p.requests.length)
  throw new Error('پاسخ صف بررسی درخواست‌های مدیریت مرکز معتبر نیست.');
 return p;
}
export function AdminManagerRequests(){
 const [list,setList]=useState<Page|null>(null),[after,setAfter]=useState<string|null>(null);
 const [refresh,setRefresh]=useState(0),[loading,setLoading]=useState(true);
 const [selected,setSelected]=useState<string|null>(null);
 const [decision,setDecision]=useState<ReviewCommand['decision']>('REVIEWED_PENDING_VERIFICATION');
 const [reason,setReason]=useState(''),[saving,setSaving]=useState(false);
 const [uncertain,setUncertain]=useState(false),[error,setError]=useState(''),[note,setNote]=useState('');
 const pending=useRef<PendingReview|null>(null),busy=useRef(false);
 useEffect(()=>{
  const controller=new AbortController();setLoading(true);setList(null);setError('');
  api(after?'?after='+encodeURIComponent(after):'',{signal:controller.signal})
   .then(validate).then(p=>{if(!controller.signal.aborted)setList(p)})
   .catch(e=>{if(!controller.signal.aborted)
    setError(e instanceof Error?e.message:'دریافت صف بررسی ناموفق بود.')})
   .finally(()=>{if(!controller.signal.aborted)setLoading(false)});
  return()=>controller.abort();
 },[after,refresh]);
 const reload=()=>{setAfter(null);setRefresh(i=>i+1)};
 function choose(item:ManagerClaim){
  if(busy.current||uncertain)return;
  setSelected(item.requestId);setDecision('REVIEWED_PENDING_VERIFICATION');
  setReason('');setError('');setNote('');
 }
 async function review(event:FormEvent){
  event.preventDefault();
  const item=list?.requests.find(r=>r.requestId===selected);
  const explanation=reason.trim();
  if(busy.current||uncertain||!item||item.state!=='PENDING_REVIEW'||
    explanation.length<10||explanation.length>500)return;
  const command:ReviewCommand={commandId:crypto.randomUUID(),decision,reason:explanation};
  pending.current={requestId:item.requestId,command};
  busy.current=true;setSaving(true);setError('');setNote('');
  try{
   const raw=await api('/'+encodeURIComponent(item.requestId)+'/review',{
    method:'POST',body:JSON.stringify(command)});
   const result=raw as ReviewResult;
   if(!result||!['Applied','Replayed'].includes(result.outcome)||
    result.requestId!==item.requestId||result.state!==decision||
    result.managerAssigned!==false)
    throw new Error('رسید بررسی ادمین معتبر نیست.');
   pending.current=null;setSelected(null);setReason('');
   setNote('بررسی انسانی ثبت شد؛ صدور دسترسی مدیریت همچنان نیازمند احراز مستقل است.');
   reload();
  }catch(e){
   setError(e instanceof Error?e.message:'ثبت بررسی ناموفق بود.');
   if(e instanceof ManagerAdminError&&e.status>=400&&e.status<500){
    pending.current=null;setSelected(null);reload();
   }else setUncertain(true);
  }finally{busy.current=false;setSaving(false)}
 }
 async function reconcile(){
  const original=pending.current;
  if(!original||busy.current)return;
  busy.current=true;setSaving(true);setError('');
  try{
   const raw=await api('/'+encodeURIComponent(original.requestId)+
    '/review-receipts/'+encodeURIComponent(original.command.commandId));
   const receipt=raw as {outcome?:string;requestId?:string;commandId?:string;
    decision?:string;reason?:string;managerAssigned?:boolean};
   if(receipt.outcome!=='Observed'||receipt.requestId!==original.requestId||
     receipt.commandId!==original.command.commandId||
     receipt.decision!==original.command.decision||
     receipt.reason!==original.command.reason||
     receipt.managerAssigned!==false)
    throw new Error('رسید بررسی ادمین با دستور ارسال‌شده مطابقت ندارد.');
   pending.current=null;setUncertain(false);setSelected(null);
   setNote('رسید همان بررسی و همان حساب ادمین تأیید شد؛ هیچ درخواستی تکرار نشد.');
   reload();
  }catch(e){
   setError(e instanceof ManagerAdminError&&e.status===404?
    'رسید همان دستور هنوز پیدا نشده است؛ هیچ درخواست جدیدی ارسال نکنید.':
    e instanceof Error?e.message:'بازخوانی وضعیت ناموفق بود.');
  }finally{busy.current=false;setSaving(false)}
 }
 const active=list?.requests.find(r=>r.requestId===selected&&r.state==='PENDING_REVIEW');
 return <section className="manager-admin" dir="rtl">
  <section className="figma-card manager-admin-alert" role="note">
   <h2>بررسی درخواست مدیران مراکز</h2>
   <p>متقاضی فقط برای یک مرکز مرجع ثبت درخواست می‌کند. تأیید هویت سازمانی و اختیار نمایندگی باید مستقل انجام شود؛ وضعیت «بررسی‌شده» هیچ دسترسی مدیریتی، بالینی یا مالی نمی‌دهد.</p>
  </section>
  {error&&<p role="alert" className="manager-admin-error">{error}</p>}
  {note&&<p role="status" className="manager-admin-note">{note}</p>}
  <section className="figma-card">
   <header><div><small>پرونده‌های PostgreSQL</small><h2>صف درخواست‌ها</h2></div>
    <button type="button" disabled={loading||saving} onClick={reload}>بازخوانی</button></header>
   {loading&&<p role="status">در حال دریافت درخواست‌ها…</p>}
   {list&&<><div className="figma-table-wrap"><table>
    <thead><tr><th>مرکز مرجع</th><th>هویت درخواست‌دهنده</th><th>توضیح ادعایی</th><th>وضعیت</th><th>اقدام</th></tr></thead>
    <tbody>{list.requests.map(r=><tr key={r.requestId}>
     <td><bdi>{r.centerRef}</bdi></td>
     <td><bdi>{r.applicantSubject}</bdi><small><bdi>{r.applicantIssuer}</bdi></small></td>
     <td>{r.reason}</td>
     <td>{labels[r.state]}{r.reviewReason&&<small>{r.reviewReason}</small>}</td>
     <td>{r.state==='PENDING_REVIEW'?
      <button type="button" disabled={saving||uncertain} onClick={()=>choose(r)}>بررسی درخواست</button>:
      <span>سابقه ثبت‌شده</span>}</td>
    </tr>)}</tbody>
   </table></div>
   {!list.requests.length&&<p>درخواستی در این صفحه ثبت نشده است.</p>}
   {list.nextAfter&&<button type="button" disabled={saving||loading||uncertain}
    onClick={()=>setAfter(list.nextAfter)}>صفحه بعد</button>}</>}
  </section>
  {active&&<form className="figma-card manager-admin-review" onSubmit={e=>void review(e)}>
   <h2>ثبت نتیجه بررسی درخواست</h2>
   <p>مرجع درخواست: <bdi>{active.requestId}</bdi></p>
   <label>نتیجه بررسی
    <select value={decision} onChange={e=>setDecision(e.target.value as ReviewCommand['decision'])}
     disabled={saving||uncertain}>
     <option value="REVIEWED_PENDING_VERIFICATION">بررسی‌شده؛ در انتظار احراز مستقل اختیار</option>
     <option value="DECLINED">رد درخواست</option>
    </select>
   </label>
   <label>علت ثبت‌شده توسط ادمین
    <textarea required rows={4} minLength={10} maxLength={500}
     value={reason} disabled={saving||uncertain}
     onChange={e=>setReason(e.target.value)}/>
   </label>
   {uncertain?<button type="button" onClick={()=>void reconcile()} disabled={saving}>بررسی نتیجه قبلی بدون ارسال دوباره</button>:
    <button type="submit" disabled={saving||reason.trim().length<10||
      reason.trim().length>500}>{saving?'در حال ثبت…':'ثبت بررسی انسانی بدون صدور دسترسی'}</button>}
   {uncertain&&<p>نتیجه قبلی نامشخص است؛ درخواست دوباره ارسال نمی‌شود.</p>}
  </form>}
 </section>;
}
