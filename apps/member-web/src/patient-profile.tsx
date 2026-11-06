import {useEffect,useRef,useState,type FormEvent} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,getMyProfile,saveMyProfile,type MemberProfile} from './taranom-api';
import './patient-profile.css';
export function PatientProfile({editing=false,web=false}:{editing?:boolean;web?:boolean}){
 const [profile,setProfile]=useState<MemberProfile|null>(null),[draft,setDraft]=useState<MemberProfile|null>(null);
 const [error,setError]=useState(''),[notice,setNotice]=useState(''),[busy,setBusy]=useState(false),[attempt,setAttempt]=useState(0),[uncertain,setUncertain]=useState(false),[stale,setStale]=useState(false);
 const pending=useRef<{requestId:string;payload:MemberProfile}|null>(null),saving=useRef(false),alive=useRef(true);
 useEffect(()=>{alive.current=true;return()=>{alive.current=false}},[]);
 useEffect(()=>{
  const controller=new AbortController();setProfile(null);setDraft(null);setError('');
  getMyProfile(controller.signal).then(p=>{if(!controller.signal.aborted){setProfile(p);setDraft(p);setStale(false)}}).catch((e:unknown)=>{if(!controller.signal.aborted)setError(e instanceof Error?e.message:'دریافت مشخصات ناموفق بود.')});
  return()=>controller.abort();
 },[attempt]);
 async function save(e:FormEvent){
  e.preventDefault();if(!draft||saving.current||uncertain||stale)return;
  saving.current=true;setBusy(true);setError('');setNotice('');
  const command={requestId:crypto.randomUUID(),payload:{...draft}};pending.current=command;
  let writeAccepted=false;
  try{
   await saveMyProfile(command.payload,command.requestId);writeAccepted=true;
   const confirmed=await getMyProfile();
   if(alive.current){setProfile(confirmed);setDraft(confirmed);setNotice('تغییرات ذخیره شد.');pending.current=null}
  }catch(e){if(alive.current){setError(e instanceof Error?e.message:'پاسخ ذخیره دریافت نشد.');if(!writeAccepted&&e instanceof AppointmentApiError&&e.status>=400&&e.status<500){pending.current=null;if(e.status===409){setStale(true);setError('اطلاعات در جای دیگری تغییر کرده است. ابتدا نسخهٔ جدید را دریافت کنید.')}}else setUncertain(true)}}
  finally{saving.current=false;if(alive.current)setBusy(false)}
 }
 async function reconcile(){
  if(saving.current||!pending.current)return;
  saving.current=true;setBusy(true);
  try{
   const current=await getMyProfile(),wanted=pending.current.payload;
   if(!alive.current)return;
   setProfile(current);setDraft(current);
   if(current.revision>wanted.revision&&current.fullName===wanted.fullName.trim()&&current.city===wanted.city.trim()&&current.birthDate===wanted.birthDate){setUncertain(false);setError('');setNotice('ذخیرهٔ تغییرات تأیید شد.');pending.current=null}
   else setError('ذخیره هنوز تأیید نشده است. نتیجه را دوباره بررسی کنید.');
  }catch{if(alive.current)setError('دریافت نتیجه ناموفق بود. دوباره بررسی کنید.')}
  finally{saving.current=false;if(alive.current)setBusy(false)}
 }
 const edit=editing||web;
 const content=<section className="patient-profile" aria-busy={busy}>
  <h2>مشخصات فردی</h2>{error&&<p role="alert" className="profile-error">{error}</p>}{notice&&<p role="status" className="profile-notice">{notice}</p>}
  {!profile&&!error&&<p role="status">در حال دریافت مشخصات…</p>}
  {draft&&<form onSubmit={e=>void save(e)}>
   <label>نام و نام خانوادگی<input required maxLength={140} readOnly={!edit} disabled={busy||uncertain||stale} value={draft.fullName} onChange={e=>setDraft({...draft,fullName:e.target.value})}/></label>
   <label>شماره همراه<input disabled value="نیازمند اتصال مرجع هویت"/></label>
   <label>کد ملی<input disabled value="نیازمند اتصال مرجع هویت"/></label>
   <label>{edit?'تاریخ تولد (میلادی)':'تاریخ تولد'}{edit?<input type="date" min="1900-01-01" disabled={busy||uncertain||stale} value={draft.birthDate??''} onChange={e=>setDraft({...draft,birthDate:e.target.value||null})}/>:<input readOnly value={draft.birthDate?new Intl.DateTimeFormat('fa-IR',{dateStyle:'long',timeZone:'UTC'}).format(new Date(draft.birthDate+'T12:00:00Z')):'ثبت نشده'}/>}</label>
   <label>شهر محل سکونت<input maxLength={100} readOnly={!edit} disabled={busy||uncertain||stale} value={draft.city} onChange={e=>setDraft({...draft,city:e.target.value})}/></label>
   <p className="profile-note">شمارهٔ همراه و کد ملی از این بخش قابل تغییر نیستند. مشخصات ثبت‌شده، اطلاعات اعلامی شماست.</p>
   {edit&&!uncertain&&<button className="profile-save" disabled={busy||stale} type="submit">{busy?'در حال ذخیره…':'ذخیرهٔ تغییرات'}</button>}
   {uncertain&&<button className="profile-save" disabled={busy} type="button" onClick={()=>void reconcile()}>بررسی نتیجه ذخیره</button>}
   {!edit&&<a className="profile-save" href="/patient-app/edit-info">ویرایش اطلاعات</a>}
  </form>}
  {!uncertain&&<button className="profile-refresh" disabled={busy} onClick={()=>{setNotice('');setAttempt(x=>x+1)}}>دریافت آخرین مشخصات</button>}
 </section>;
 return web?<section className="portal-page"><header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>اطلاعات حساب کاربری</h1><p>مشخصات اعلامی خود را مدیریت کنید.</p></div></header>{content}</section>:<AppointmentPage title={editing?'ویرایش اطلاعات':'اطلاعات فردی'} subtitle="مشخصات حساب ترنم" activeSection="account">{content}</AppointmentPage>;
}
