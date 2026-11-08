import {patientServices} from './patient-services';
import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,getMyAppointments,getMyNotifications,getMyProfile,type MemberProfile} from './taranom-api';
import './patient-home.css';
type Overview={profile:MemberProfile|null;upcoming:number|null;unread:number|undefined;errors:string[]};
const empty:Overview={profile:null,upcoming:null,unread:undefined,errors:[]};
const number=(n:number)=>new Intl.NumberFormat('fa-IR').format(n);
const tiles=[
 {label:'نوبت‌های من',icon:'calendar',href:patientServices.appointments.href},
 {label:'گرفتن نوبت',icon:'calendar',href:patientServices.booking.href},
 {label:'پروندهٔ سلامت',icon:'record',href:patientServices.records.href},
 {label:'نسخه‌های من',icon:'prescription',href:patientServices.prescriptions.href},
 {label:'ارجاع درمانی',icon:'referral',href:patientServices.referrals.href},
 {label:'حمایت درمانی',icon:'wallet',href:patientServices.finance.href},
];
export function PatientHome(){
 const [overview,setOverview]=useState<Overview>(empty),[loading,setLoading]=useState(true),[attempt,setAttempt]=useState(0),[servicesOpen,setServicesOpen]=useState(false);
 useEffect(()=>{document.title='ترنم | خانه'},[]);
 useEffect(()=>{
  const controller=new AbortController();setOverview(empty);setLoading(true);
  Promise.allSettled([getMyProfile(controller.signal),getMyAppointments(controller.signal),getMyNotifications({},controller.signal)]).then(([profile,appointments,notifications])=>{
   if(controller.signal.aborted)return;
   const failures=[profile,appointments,notifications].filter(r=>r.status==='rejected');
   const sessionMissing=failures.some(r=>r.status==='rejected'&&r.reason instanceof AppointmentApiError&&r.reason.status===401);
   // A rejected identity session invalidates the entire overview, even if another request completed first.
   if(sessionMissing){setOverview({...empty,errors:['برای نمایش اطلاعات حساب، نشست ورود معتبر لازم است.']});setLoading(false);return}
   const errors:string[]=[];
   if(profile.status==='rejected')errors.push('دریافت مشخصات حساب ناموفق بود.');
   if(appointments.status==='rejected')errors.push('دریافت نوبت‌های پیش‌رو ناموفق بود.');
   if(notifications.status==='rejected')errors.push('دریافت تعداد اعلان‌ها ناموفق بود.');
   setOverview({profile:profile.status==='fulfilled'?profile.value:null,upcoming:appointments.status==='fulfilled'?appointments.value.appointments.filter(a=>a.state==='Booked'&&Date.parse(a.startsAt)>Date.now()).length:null,unread:notifications.status==='fulfilled'?notifications.value.unreadCount:undefined,errors});setLoading(false);
  });
  return()=>controller.abort();
 },[attempt]);
 const name=overview.profile?.fullName.trim().split(/\s+/)[0];
 return <AppointmentPage title={name?'سلام، '+name:'سلام'} subtitle="همهٔ خدمات درمانی و مالی در یک‌جا" activeSection="home" unreadCount={overview.unread}><section className="patient-home" aria-label="خانهٔ ترنم">
  {loading&&<p className="home-load" role="status">در حال دریافت اطلاعات حساب…</p>}
  {overview.errors.length>0&&<section className="home-errors" role="alert">{overview.errors.map(e=><p key={e}>{e}</p>)}<button onClick={()=>setAttempt(x=>x+1)}>تلاش دوباره</button></section>}
  <a className="home-search" href="/patient-app/booking"><span>مشاهدهٔ مراکز و نوبت‌گیری</span><img src="/assets/home/search.svg" width="22" height="22" alt=""/></a>
  <section className="home-guide"><div className="home-guide-heading"><span className="home-cross"><img src="/assets/home/cross.svg" width="18" height="18" alt=""/></span><span className="home-guide-label">راهنمای هوشمند</span></div><h2>راهنمای انتخاب تخصص</h2><p>برای یافتن تخصص مناسب؛ تشخیص با پزشک است.</p><button disabled title="راهنمای هوشمند هنوز فعال نشده است.">هنوز فعال نیست</button></section>
  <div className="home-services-heading"><h2>خدمات ترنم</h2><button aria-expanded={servicesOpen} aria-controls="home-service-links" onClick={()=>setServicesOpen(x=>!x)}>همهٔ خدمات</button></div>
  <div className="home-services">{tiles.map(tile=>{
   const content=<><span className="home-tile-copy"><span>{tile.label}</span>{tile.label==='نوبت‌های من'&&overview.upcoming!==null&&<small id="home-upcoming" className="home-upcoming">{number(overview.upcoming)} نوبت پیش‌رو</small>}{!tile.href&&<small>در حال آماده‌سازی</small>}</span><span className="home-icon"><img src={'/assets/home/'+tile.icon+'.svg'} width="20" height="20" alt=""/></span></>;
   return tile.href?<a key={tile.label} className="home-tile" aria-label={tile.label} aria-describedby={tile.label==='نوبت‌های من'&&overview.upcoming!==null?'home-upcoming':undefined} href={tile.href}>{content}</a>:<button disabled key={tile.label} className="home-tile" aria-label={tile.label} title="این خدمت هنوز به بک‌اند متصل نشده است.">{content}</button>;
  })}</div>
  <section className="home-finance"><div><h2>مالی و حمایت درمان</h2><p>اطلاعات مالی در دسترس نیست.</p><small>سهم بیمه پس از استعلام معتبر نمایش داده می‌شود.</small></div><span className="home-icon"><img src="/assets/home/wallet.svg" width="20" height="20" alt=""/></span></section>
  {servicesOpen&&<section className="home-service-links" id="home-service-links"><h2>خدمات متصل حساب</h2><a href="/patient-app/services">فهرست همهٔ خدمات</a><a href="/patient-app/account">حساب من</a><a href="/patient-app/personal-info">اطلاعات فردی</a><a href="/patient-app/support">پشتیبانی</a><a href="/patient-app/notifications">اعلان‌ها</a><p>دکمه‌های غیرفعال هنوز آمادهٔ استفاده نیستند.</p><button disabled={loading} onClick={()=>setAttempt(x=>x+1)}>تازه‌سازی خانه</button></section>}
 </section></AppointmentPage>;
}
