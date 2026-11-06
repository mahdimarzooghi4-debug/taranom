import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {getAvailableAppointmentCenters,type AvailableAppointmentCenter} from './taranom-api';
import './appointment-discovery.css';

type DiscoveryState=
 |{kind:'loading'}
 |{kind:'error';message:string}
 |{kind:'ready';centers:AvailableAppointmentCenter[]};
const formatDate=(value:string)=>new Intl.DateTimeFormat('fa-IR',{
 dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'
}).format(new Date(value));

export function AppointmentDiscovery(){
 const [state,setState]=useState<DiscoveryState>({kind:'loading'});
 const [refresh,setRefresh]=useState(0),[query,setQuery]=useState('');
 useEffect(()=>{
  document.title='ترنم | مراکز دارای ظرفیت نوبت';
  const controller=new AbortController();
  setState({kind:'loading'});
  getAvailableAppointmentCenters(controller.signal)
   .then(centers=>{if(!controller.signal.aborted)setState({kind:'ready',centers})})
   .catch((error:unknown)=>{if(!controller.signal.aborted)
    setState({kind:'error',message:error instanceof Error?error.message:'دریافت ظرفیت مراکز ناموفق بود.'})});
  return()=>controller.abort();
 },[refresh]);
 const shown=state.kind==='ready'?state.centers.filter(c=>c.centerRef.includes(query.trim())):[];
 return <AppointmentPage title="مراکز دارای ظرفیت" subtitle="انتخاب مرکز از برنامهٔ نوبت‌دهی ثبت‌شده">
  <section className="booking-note" aria-label="مرز اطلاعات مراکز">
   <h2>اطلاعات این فهرست فقط ظرفیت ثبت‌شدهٔ مراکز است.</h2>
   <p>نام، شهر، تخصص و صلاحیت پزشکان در این مرحله تأیید نشده‌اند و نمایش داده نمی‌شوند. ثبت نوبت پس از انتخاب زمان و تأیید شما انجام می‌شود.</p>
  </section>
  {state.kind==='loading'&&<p role="status" className="appointment-state">در حال دریافت مراکز دارای ظرفیت…</p>}
  {state.kind==='error'&&<section className="appointment-state"><p role="alert">{state.message}</p><button type="button" className="appointment-refresh" onClick={()=>setRefresh(n=>n+1)}>تلاش دوباره</button></section>}
  {state.kind==='ready'&&<>
   <label className="booking-center-search">جست‌وجو بر اساس مرجع مرکز
    <input type="search" dir="ltr" value={query} onChange={e=>setQuery(e.target.value)} placeholder="مرجع مرکز" />
   </label>
   <p className="booking-center-footnote">حداکثر ۵۰ مرکز با نزدیک‌ترین ظرفیت باز از برنامه‌های ثبت‌شده نمایش داده می‌شود.</p>
   {state.centers.length===0&&<p role="status" className="appointment-state">در حال حاضر ظرفیت آزادی در برنامه‌های ثبت‌شده یافت نشد.</p>}
   {state.centers.length>0&&shown.length===0&&<p role="status" className="appointment-state">مرکزی با این مرجع پیدا نشد.</p>}
   <div className="booking-centers">
    {shown.map(center=><article key={center.centerRef} className="booking-provider booking-center-card">
     <h2>مرکز با مرجع <bdi>{center.centerRef}</bdi></h2>
     <p>ظرفیت‌های باز ثبت‌شده: {center.openSlotCount.toLocaleString('fa-IR')}</p>
     <p>نزدیک‌ترین زمان: <time dateTime={center.nextAvailableAt}>{formatDate(center.nextAvailableAt)}</time></p>
     <a className="appointment-primary booking-center-link" href={'/patient-app/slots?'+new URLSearchParams({center:center.centerRef})}>انتخاب زمان این مرکز</a>
    </article>)}
   </div>
  </>}
  <a className="appointment-refresh" href="/patient-app/appointments">نوبت‌های من</a>
 </AppointmentPage>;
}
