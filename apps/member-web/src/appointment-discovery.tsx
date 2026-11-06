import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {getAvailableAppointmentCenters} from './taranom-api';
import './appointment-discovery.css';

type DiscoveryState=
 |{kind:'loading'}
 |{kind:'error';message:string}
 |{kind:'ready'};

export function AppointmentDiscovery(){
 const [state,setState]=useState<DiscoveryState>({kind:'loading'});
 const [refresh,setRefresh]=useState(0);
 useEffect(()=>{
  document.title='ترنم | وضعیت انتشار مراکز درمانی';
  const controller=new AbortController();
  setState({kind:'loading'});
  getAvailableAppointmentCenters(controller.signal)
   .then(()=>{if(!controller.signal.aborted)setState({kind:'ready'})})
   .catch((error:unknown)=>{if(!controller.signal.aborted)
    setState({kind:'error',message:error instanceof Error?error.message:'دریافت ظرفیت مراکز ناموفق بود.'})});
  return()=>controller.abort();
 },[refresh]);
 return <AppointmentPage title="مراکز درمانی" subtitle="انتشار مراکز نیازمند مجوز مستقل است">
  <section className="booking-note" aria-label="مرز اطلاعات مراکز">
   <h2>ثبت مرکز در ترنم، مجوز ارائه خدمات نیست.</h2>
   <p>تا زمان احراز صلاحیت و مجوز مستقل انتشار، فهرست مراکز برای انتخاب نوبت فعال نیست. ظرفیت داخلی نیز به‌تنهایی مجوز نمایش مرکز محسوب نمی‌شود.</p>
  </section>
  {state.kind==='loading'&&<p role="status" className="appointment-state">در حال دریافت مراکز دارای ظرفیت…</p>}
  {state.kind==='error'&&<section className="appointment-state"><p role="alert">{state.message}</p><button type="button" className="appointment-refresh" onClick={()=>setRefresh(n=>n+1)}>تلاش دوباره</button></section>}
  {state.kind==='ready'&&<p role="status" className="appointment-state">
   فهرست مراکز هنوز برای انتشار و نوبت‌گیری عمومی تأیید نشده است.
  </p>}
  <a className="appointment-refresh" href="/patient-app/appointments">نوبت‌های من</a>
 </AppointmentPage>;
}
