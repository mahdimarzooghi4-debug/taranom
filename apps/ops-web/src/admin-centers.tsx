import {useEffect,useRef,useState,type FormEvent} from 'react';
import './admin-centers.css';

type Center={centerRef:string;displayName:string;city:string;recordState:'ADMIN_DEFINED_UNVERIFIED';
 licensed:false;published:false;managerAssigned:false};
type Page={centers:Center[];nextAfter:string|null};
type Receipt={outcome:'Applied'|'Replayed';centerRef:string;displayName:string;city:string;
 recordState:'ADMIN_DEFINED_UNVERIFIED';licensed:false;published:false;managerAssigned:false};
class RegistryError extends Error{
 constructor(readonly status:number){super(status===401?'نشست ورود معتبر لازم است.':
  status===403?'مجوز تعریف مراکز را ندارید.':
  status===409?'این شناسه درخواست با اطلاعات دیگری قبلاً استفاده شده است.':
  status===400?'اطلاعات ثبت مرکز معتبر نیست.':'ارتباط با رجیستری مراکز ناموفق بود.')}
}
declare global{interface Window{taranomSession?:{getAccessToken:()=>string|Promise<string>}}}
async function api(path:string,body?:unknown,signal?:AbortSignal):Promise<unknown>{
 const token=await window.taranomSession?.getAccessToken();if(!token)throw new RegistryError(401);
 const result=await fetch('/api/v1/operations/centers'+path,{
  method:body?'POST':'GET',
  headers:{Accept:'application/json',Authorization:'Bearer '+token,...(body?{'Content-Type':'application/json'}:{})},
  body:body?JSON.stringify(body):undefined,redirect:'error',credentials:'omit',cache:'no-store',signal
 });
 if(!result.ok)throw new RegistryError(result.status);
 return result.json() as Promise<unknown>;
}
function validCenter(x:unknown):x is Center{
 const c=x as Center;
 return !!c&&typeof c.centerRef==='string'&&/^center-[a-f0-9]{32}$/.test(c.centerRef)&&
  typeof c.displayName==='string'&&c.displayName.length>0&&c.displayName.length<=140&&
  typeof c.city==='string'&&c.city.length>0&&c.city.length<=100&&
  c.recordState==='ADMIN_DEFINED_UNVERIFIED'&&c.licensed===false&&
  c.published===false&&c.managerAssigned===false;
}
function validPage(raw:unknown):Page{
 const r=raw as Page;
 if(!r||!Array.isArray(r.centers)||r.centers.length>20||!r.centers.every(validCenter)||
  !(r.nextAfter===null||typeof r.nextAfter==='string'&&/^center-[a-f0-9]{32}$/.test(r.nextAfter))||
  new Set(r.centers.map(c=>c.centerRef)).size!==r.centers.length)
  throw new Error('فهرست مرجع مراکز معتبر نیست.');
 return r;
}
function validReceipt(raw:unknown,command:{displayName:string;city:string}):Receipt{
 const r=raw as Receipt;
 if(!r||!['Applied','Replayed'].includes(r.outcome)||!validCenter(r)||
  r.displayName!==command.displayName||r.city!==command.city)
  throw new Error('رسید ایجاد مرکز معتبر نیست.');
 return r;
}
type Pending={requestId:string;displayName:string;city:string};
export function AdminCenters(){
 const [list,setList]=useState<Page|null>(null),[after,setAfter]=useState<string|null>(null);
 const [refresh,setRefresh]=useState(0),[loading,setLoading]=useState(true);
 const [name,setName]=useState(''),[city,setCity]=useState('');
 const [saving,setSaving]=useState(false),[uncertain,setUncertain]=useState(false);
 const [error,setError]=useState(''),[notice,setNotice]=useState('');
 const pending=useRef<Pending|null>(null),busy=useRef(false);
 useEffect(()=>{
  const controller=new AbortController();setList(null);setLoading(true);setError('');
  api(after?'?after='+encodeURIComponent(after):'',undefined,controller.signal)
   .then(validPage).then(result=>{if(!controller.signal.aborted)setList(result)})
   .catch(e=>{if(!controller.signal.aborted)setError(e instanceof Error?e.message:'دریافت مراکز ناموفق بود.')})
   .finally(()=>{if(!controller.signal.aborted)setLoading(false)});
  return()=>controller.abort();
 },[after,refresh]);
 const load=()=>{setAfter(null);setRefresh(n=>n+1)};
 async function create(e:FormEvent){
  e.preventDefault();const displayName=name.trim(),cityText=city.trim();
  if(busy.current||uncertain||!displayName||!cityText)return;
  const request={requestId:crypto.randomUUID(),displayName,city:cityText};
  pending.current=request;busy.current=true;setSaving(true);setError('');setNotice('');
  try{
   const receipt=validReceipt(await api('',request),request);
   pending.current=null;setName('');setCity('');
   setNotice('مرکز با شناسه '+receipt.centerRef+' ثبت شد؛ تأیید صلاحیت و انتشار انجام نشده است.');
   load();
  }catch(e){
   setError(e instanceof Error?e.message:'ثبت مرکز ناموفق بود.');
   if(e instanceof RegistryError&&e.status>=400&&e.status<500)pending.current=null;
   else setUncertain(true);
  }finally{busy.current=false;setSaving(false)}
 }
 async function reconcile(){
  const original=pending.current;if(!original||busy.current)return;
  busy.current=true;setSaving(true);setError('');
  try{
   const result=await api('/creation-requests/'+encodeURIComponent(original.requestId)) as
    {outcome?:string;centerRef?:string;recordState?:string};
   if(result.outcome!=='Observed'||result.recordState!=='ADMIN_DEFINED_UNVERIFIED'||
    typeof result.centerRef!=='string'||!/^center-[a-f0-9]{32}$/.test(result.centerRef))
    throw new Error('رسید قابل تأیید نیست.');
   pending.current=null;setUncertain(false);setName('');setCity('');
   setNotice('ثبت مرکز با شناسه '+result.centerRef+' از پایگاه داده تأیید شد.');load();
  }catch(e){
   setError(e instanceof RegistryError&&e.status===404?'رسید هنوز یافت نشد؛ ثبت را تکرار نکنید.':
    e instanceof Error?e.message:'بررسی نتیجه ثبت ناموفق بود.');
  }finally{busy.current=false;setSaving(false)}
 }
 return <section className="admin-centers" dir="rtl">
  <section className="figma-card admin-center-warning">
   <h2>تعریف مرکز فقط با مجوز ادمین</h2>
   <p>ایجاد رکورد مرکز به‌معنای تأیید مجوز، قرارداد، کیفیت خدمت، دسترسی مدیر مرکز یا نمایش در جست‌وجوی بیماران نیست. اتصال مدیر مرکز پس از احراز هویت و تصویب قرارداد مجوز انجام می‌شود.</p>
  </section>
  {error&&<p role="alert" className="admin-center-error">{error}</p>}
  {notice&&<p role="status" className="admin-center-success">{notice}</p>}
  <form className="figma-card admin-center-form" onSubmit={e=>void create(e)}>
   <h2>تعریف مرکز درمانی</h2>
   <label>نام اعلامی مرکز<input required maxLength={140} value={name} onChange={e=>setName(e.target.value)}
    disabled={saving||uncertain||loading||!list}/></label>
   <label>شهر اعلامی مرکز<input required maxLength={100} value={city} onChange={e=>setCity(e.target.value)}
    disabled={saving||uncertain||loading||!list}/></label>
   {uncertain?
    <button type="button" onClick={()=>void reconcile()} disabled={saving}>بررسی نتیجه ثبت</button>:
    <button type="submit" disabled={saving||loading||!list||!name.trim()||!city.trim()}>{saving?'در حال ثبت…':'ایجاد مرکز با شناسه مرجع'}</button>}
   {uncertain&&<p>نتیجه ثبت نامطمئن است؛ هیچ درخواست دومی ارسال نمی‌شود.</p>}
  </form>
  <section className="figma-card">
   <header><div><small>دادهٔ مرجع PostgreSQL</small><h2>مراکز تعریف‌شده توسط ادمین</h2></div>
    <button type="button" disabled={saving||loading||uncertain} onClick={load}>بازخوانی فهرست</button></header>
   {loading&&<p role="status">در حال بارگذاری مراکز…</p>}
   {list&&<><div className="figma-table-wrap"><table>
    <thead><tr><th>شناسه مرجع</th><th>نام اعلامی</th><th>شهر اعلامی</th><th>وضعیت</th><th>مدیر مرکز</th></tr></thead>
    <tbody>{list.centers.map(c=><tr key={c.centerRef}><td><bdi>{c.centerRef}</bdi></td><td>{c.displayName}</td><td>{c.city}</td><td>تعریف‌شده؛ تأییدنشده</td><td>تخصیص داده نشده</td></tr>)}</tbody>
   </table></div>
   {!list.centers.length&&<p>مرکزی در این صفحه یافت نشد.</p>}
   {list.nextAfter&&<button type="button" onClick={()=>setAfter(list.nextAfter)}
    disabled={saving||uncertain}>صفحه بعد مراکز</button>}</>}
  </section>
 </section>;
}
