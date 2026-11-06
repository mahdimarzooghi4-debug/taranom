import {useEffect,useRef,useState,type FormEvent} from 'react';
import {getDirectoryScopes,getCenterDirectoryDraft,saveCenterDirectoryDraft,
 getDoctorDirectoryDrafts,saveDoctorDirectoryDraft,TaranomApiError,
 type CenterDraftProfile,type DoctorDraft,type DirectoryScope} from './taranom-api';
import './center-directory-draft.css';

type Props={kind:'profile'|'doctors'};
type Status='loading'|'ready'|'saving'|'uncertain'|'blocked';
const capability=(kind:Props['kind'])=>kind==='profile'?'CenterAccessGrantsManage':'CenterRosterManage';
export function CenterDirectoryDraftWorkspace({kind}:Props){
 const [scopes,setScopes]=useState<DirectoryScope[]|null>(null);
 const [center,setCenter]=useState(''),[error,setError]=useState(''),[note,setNote]=useState('');
 const [status,setStatus]=useState<Status>('loading');
 const [refresh,setRefresh]=useState(0);
 const [profile,setProfile]=useState<CenterDraftProfile>({revision:0,displayName:'',city:''});
 const [doctors,setDoctors]=useState<DoctorDraft[]>([]),[nextAfter,setNextAfter]=useState<string|null>(null);
 const [doctorRef,setDoctorRef]=useState(''),[revision,setRevision]=useState(0);
 const [displayName,setDisplayName]=useState(''),[detail,setDetail]=useState('');
 const busy=useRef(false),alive=useRef(true);
 const activeCenter=useRef(''),requestGeneration=useRef(0);
 useEffect(()=>{alive.current=true;return()=>{alive.current=false}},[]);
 const selectCenter=(value:string)=>{activeCenter.current=value;requestGeneration.current++;
  setCenter(value);setError('');setNote('')};
 const reload=()=>{requestGeneration.current++;setRefresh(n=>n+1)};
 useEffect(()=>{
  let live=true;activeCenter.current='';requestGeneration.current++;
  setError('');setNote('');setStatus('loading');setCenter('');setScopes(null);
  getDirectoryScopes().then(items=>{if(!live)return;const allowed=items.filter(s=>s.capability===capability(kind));
   const initial=allowed.length===1?allowed[0].scopeRef:'';
   setScopes(allowed);activeCenter.current=initial;setCenter(initial);
   if(!allowed.length)setStatus('ready');
  }).catch(e=>{if(live){setStatus('blocked');setError(e instanceof Error?e.message:'دریافت مجوز ناموفق بود.')}});
  return()=>{live=false};
 },[kind]);
 useEffect(()=>{
  if(!center)return;
  const generation=++requestGeneration.current;
  const controller=new AbortController();
  setError('');setStatus('loading');setProfile({revision:0,displayName:'',city:''});
  setDoctors([]);setNextAfter(null);setDoctorRef('');setRevision(0);setDisplayName('');setDetail('');
  const load=kind==='profile'?getCenterDirectoryDraft(center,controller.signal):getDoctorDirectoryDrafts(center,undefined,controller.signal);
  load.then(result=>{if(controller.signal.aborted)return;
   if(kind==='profile'){const p=result as CenterDraftProfile;setProfile(p);setDisplayName(p.displayName);setDetail(p.city)}
   else{const p=result as Awaited<ReturnType<typeof getDoctorDirectoryDrafts>>;setDoctors(p.doctors);setNextAfter(p.nextAfter)}
   setStatus('ready');
  }).catch(e=>{if(!controller.signal.aborted){setStatus('blocked');setError(e instanceof Error?e.message:'دریافت اطلاعات ناموفق بود.')}});
  return()=>{controller.abort();if(requestGeneration.current===generation)requestGeneration.current++};
 },[center,kind,refresh]);
 async function save(e:FormEvent){
  e.preventDefault();
  if(busy.current||status!=='ready'||!center||!displayName.trim()||!detail.trim()||(kind==='doctors'&&!doctorRef.trim()))return;
  busy.current=true;setStatus('saving');setError('');setNote('');
  try{
   const command={requestId:crypto.randomUUID(),expectedRevision:kind==='profile'?profile.revision:revision,
     displayName:displayName.trim()};
   if(kind==='profile')await saveCenterDirectoryDraft(center,{...command,city:detail.trim()});
   else await saveDoctorDirectoryDraft(center,doctorRef.trim(),{...command,specialty:detail.trim()});
   if(alive.current){setStatus('ready');setNote('اطلاعات اعلامی با رسید ثبت شد؛ هویت و صلاحیت همچنان تأیید نشده‌اند.');reload()}
  }catch(e){
   if(alive.current){
    if(e instanceof TaranomApiError&&[400,401,403,404,409].includes(e.status)){
     setStatus('blocked');setError(e.message+' برای ادامه، پرونده را بازخوانی کنید.');
    }else{
     setStatus('uncertain');setError((e instanceof Error?e.message:'ثبت ناموفق بود.')+' نتیجه ذخیره نامطمئن است؛ بدون ارسال دوباره، بازخوانی کنید.');
    }
   }
  }finally{busy.current=false}
 }
 async function more(){
  if(!nextAfter||busy.current||status!=='ready')return;
  busy.current=true;
  const cursor=nextAfter,centerSnapshot=center,generation=requestGeneration.current;
  const controller=new AbortController();
  try{
   const result=await getDoctorDirectoryDrafts(centerSnapshot,cursor,controller.signal);
   if(alive.current&&activeCenter.current===centerSnapshot&&requestGeneration.current===generation){
    setDoctors(prev=>[...prev,...result.doctors]);setNextAfter(result.nextAfter)
   }
  }catch(e){if(alive.current&&activeCenter.current===centerSnapshot&&requestGeneration.current===generation)
   setError(e instanceof Error?e.message:'ادامه فهرست ناموفق بود.')}
  finally{busy.current=false}
 }
 const choose=(record:DoctorDraft)=>{setDoctorRef(record.doctorRef);setRevision(record.revision);setDisplayName(record.displayName);setDetail(record.specialty);setError('');setNote('')};
 const allowed=scopes?.filter(s=>s.capability===capability(kind))??[];
 return <div className="directory-draft" dir="rtl">
  <section className="figma-card directory-warning" role="note">
   <strong>پرونده اعلامی ـ تأییدنشده</strong>
   <p>داده‌های این بخش صرفاً از حساب مجاز مرکز ثبت می‌شوند. ثبت نام پزشک یا تخصص، تأیید پروانه، هویت، قرارداد، مجوز بالینی یا انتشار در جست‌وجوی بیماران نیست.</p>
  </section>
  {error&&<p role="alert" className="directory-error">{error}</p>}
  {note&&<p role="status" className="directory-note">{note}</p>}
  {scopes===null&&status==='loading'&&<p role="status">در حال بررسی دسترسی مرکز…</p>}
  {scopes!==null&&allowed.length===0&&<p role="status">مجوز فعال برای این بخش در دسترس نیست.</p>}
  {allowed.length>1&&<label className="directory-field">مرکز دارای مجوز
   <select value={center} onChange={e=>selectCenter(e.target.value)}>
    <option value="">انتخاب مرکز</option>{allowed.map(s=><option key={s.scopeRef} value={s.scopeRef}>{s.scopeRef}</option>)}
   </select></label>}
  {center&&<>
   <p className="directory-center">مرجع مرکز: <bdi>{center}</bdi></p>
   {status==='loading'&&<p role="status">در حال دریافت پرونده اعلامی…</p>}
   {kind==='doctors'&&['ready','saving','uncertain'].includes(status)&&<section className="figma-card">
    <h2>پزشکان معرفی‌شده توسط این مرکز</h2>
    {!doctors.length&&<p>هنوز پزشک اعلامی ثبت نشده است.</p>}
    {doctors.length>0&&<div className="figma-table-wrap"><table><thead><tr><th>مرجع پزشک</th><th>نام اعلامی</th><th>تخصص اعلامی</th><th>وضعیت</th><th>ویرایش</th></tr></thead><tbody>{doctors.map(d=>
     <tr key={d.doctorRef}><td><bdi>{d.doctorRef}</bdi></td><td>{d.displayName}</td><td>{d.specialty}</td><td>تأییدنشده</td><td><button type="button" onClick={()=>choose(d)} disabled={status!=='ready'}>ویرایش پیش‌نویس</button></td></tr>
    )}</tbody></table></div>}
    {nextAfter&&<button type="button" onClick={()=>void more()} disabled={status!=='ready'}>صفحه بعد فهرست</button>}
   </section>}
   {['ready','saving','uncertain'].includes(status)&&<form className="figma-card directory-form" onSubmit={e=>void save(e)}>
    <h2>{kind==='profile'?'مشخصات اعلامی مرکز':'ثبت/ویرایش پزشک اعلامی'}</h2>
    {kind==='doctors'&&<label className="directory-field">شناسه مرجع پزشک در مرکز
     <input required maxLength={256} value={doctorRef} disabled={revision>0||status!=='ready'} onChange={e=>setDoctorRef(e.target.value)}/>
    </label>}
    <label className="directory-field">{kind==='profile'?'نام اعلامی مرکز':'نام اعلامی پزشک'}
     <input required maxLength={140} value={displayName} onChange={e=>setDisplayName(e.target.value)} disabled={status!=='ready'}/>
    </label>
    <label className="directory-field">{kind==='profile'?'شهر اعلامی':'تخصص اعلامی'}
     <input required maxLength={kind==='profile'?100:120} value={detail} onChange={e=>setDetail(e.target.value)} disabled={status!=='ready'}/>
    </label>
    <p>نسخه فعلی: {(kind==='profile'?profile.revision:revision).toLocaleString('fa-IR')} · وضعیت: پیش‌نویس تأییدنشده</p>
    <button type="submit" className="figma-form-submit" disabled={status!=='ready'||!displayName.trim()||!detail.trim()||(kind==='doctors'&&!doctorRef.trim())}>{status==='saving'?'در حال ثبت…':'ثبت پیش‌نویس با کنترل نسخه'}</button>
    {kind==='doctors'&&<button type="button" className="directory-reset" onClick={()=>{setDoctorRef('');setRevision(0);setDisplayName('');setDetail('')}} disabled={status!=='ready'}>پزشک جدید</button>}
    {status==='uncertain'&&<p>نتیجه قبلی نامطمئن است؛ درخواست تکرار نمی‌شود.</p>}
   </form>}
   <button type="button" className="directory-reset" onClick={reload}>بازخوانی از پایگاه داده</button>
  </>}
 </div>;
}
