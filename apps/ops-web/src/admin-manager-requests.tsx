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
type CaseWorkspace={
 requestId:string;
 center:{centerRef:string;displayName:string;city:string;definedByIssuer:string;
  definedBySubject:string;recordState:'ADMIN_DEFINED_UNVERIFIED';licensed:false;published:false};
 applicant:{issuer:string;subject:string;submittedReason:string;submittedAt:string};
 review:{state:ReviewState;reason:string|null;reviewerIssuer:string|null;
  reviewerSubject:string|null;reviewedAt:string|null};
 otherApplicantClaims:number;
 representationEvidence:{sourceStatus:'NOT_CONNECTED';authorityVerified:false;criteriaDefined:false};
 associationGateRequired:true;managerAssignedByThisWorkflow:false;
};
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
function validateWorkspace(raw:unknown,expected:ManagerClaim):CaseWorkspace{
 const w=raw as CaseWorkspace;
 if(!w||w.requestId!==expected.requestId||!w.center||
  w.center.centerRef!==expected.centerRef||
  w.center.recordState!=='ADMIN_DEFINED_UNVERIFIED'||w.center.licensed!==false||
  w.center.published!==false||
  typeof w.center.displayName!=='string'||!w.center.displayName.trim()||
  typeof w.center.city!=='string'||!w.center.city.trim()||
  typeof w.center.definedByIssuer!=='string'||
  typeof w.center.definedBySubject!=='string'||
  !w.applicant||w.applicant.issuer!==expected.applicantIssuer||
  w.applicant.subject!==expected.applicantSubject||
  w.applicant.submittedReason!==expected.reason||
  !Number.isFinite(Date.parse(w.applicant.submittedAt))||
  !w.review||w.review.state!==expected.state||
  !states.includes(w.review.state)||
  !(w.review.reason===null||typeof w.review.reason==='string')||
  !(w.review.reviewerIssuer===null||typeof w.review.reviewerIssuer==='string')||
  !(w.review.reviewerSubject===null||typeof w.review.reviewerSubject==='string')||
  !(w.review.reviewedAt===null||typeof w.review.reviewedAt==='string'&&
    Number.isFinite(Date.parse(w.review.reviewedAt)))||
  !Number.isSafeInteger(w.otherApplicantClaims)||w.otherApplicantClaims<0||
  !w.representationEvidence||w.representationEvidence.sourceStatus!=='NOT_CONNECTED'||
  w.representationEvidence.authorityVerified!==false||
  w.representationEvidence.criteriaDefined!==false||
  w.associationGateRequired!==true||w.managerAssignedByThisWorkflow!==false)
  throw new Error('پرونده مدیر مرکز معتبر نیست یا در زمان بررسی تغییر کرده است؛ فهرست را بازخوانی کنید.');
 return w;
}
export function AdminManagerRequests(){
 const [list,setList]=useState<Page|null>(null),[after,setAfter]=useState<string|null>(null);
 const [refresh,setRefresh]=useState(0),[loading,setLoading]=useState(true);
 const [selected,setSelected]=useState<string|null>(null);
 const [workspace,setWorkspace]=useState<CaseWorkspace|null>(null);
 const [workspaceLoading,setWorkspaceLoading]=useState(false);
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
 useEffect(()=>{
  const current=selected?list?.requests.find(r=>r.requestId===selected):undefined;
  if(!current){setWorkspace(null);setWorkspaceLoading(false);return;}
  const controller=new AbortController();
  setWorkspace(null);setWorkspaceLoading(true);
  api('/'+encodeURIComponent(current.requestId)+'/workspace',{signal:controller.signal})
   .then(raw=>validateWorkspace(raw,current))
   .then(item=>{if(!controller.signal.aborted)setWorkspace(item)})
   .catch(e=>{if(!controller.signal.aborted)
    setError(e instanceof Error?e.message:'دریافت پرونده مدیر مرکز ناموفق بود.')})
   .finally(()=>{if(!controller.signal.aborted)setWorkspaceLoading(false)});
  return()=>controller.abort();
 },[selected,list]);
 const reload=()=>{setSelected(null);setWorkspace(null);setAfter(null);setRefresh(i=>i+1)};
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
    workspace?.requestId!==item.requestId||
    workspace.review.state!=='PENDING_REVIEW'||
    workspace.center.centerRef!==item.centerRef||
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
 const active=list?.requests.find(r=>r.requestId===selected&&r.state==='PENDING_REVIEW'&&
  workspace?.requestId===r.requestId&&workspace.review.state==='PENDING_REVIEW');
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
  {selected&&workspaceLoading&&
   <p role="status">در حال بارگذاری پرونده مرجع مرکز…</p>}
  {workspace&&<section className="figma-card manager-admin-workspace">
   <h2>پرونده مرجع برای بررسی انسانی</h2>
   <p>مرکز تعریف‌شده توسط ادمین: <strong>{workspace.center.displayName}</strong>،
    شهر اعلامی: <strong>{workspace.center.city}</strong></p>
   <p>شناسه مرکز: <bdi>{workspace.center.centerRef}</bdi></p>
   <p>صادرکننده هویت متقاضی: <bdi>{workspace.applicant.issuer}</bdi>،
    شناسه حساب متقاضی: <bdi>{workspace.applicant.subject}</bdi></p>
   <p>شرح درخواست: {workspace.applicant.submittedReason}</p>
   <p>تعداد درخواست‌های دیگر برای این مرکز: {workspace.otherApplicantClaims}</p>
   {workspace.otherApplicantClaims>0&&
    <p role="note">چند متقاضی برای همین مرکز وجود دارند؛ این موضوع باید به‌صورت انسانی بررسی شود.</p>}
   <p className="manager-admin-no-proof">مرجع مستقل احراز اختیار سازمانی متصل نیست و معیار
    تصویب انتساب مدیر تعریف نشده است. اطلاعات مرکز نیز تأیید صلاحیت پزشکی یا انتشار نیست.</p>
   <p>نتیجه این بررسی به‌تنهایی هیچ مجوز مدیریتی صادر نمی‌کند.</p>
  </section>}
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
