import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,getMyProfile,type MemberProfile} from './taranom-api';
import './patient-account.css';

export function PatientAccount(){
 const [profile,setProfile]=useState<MemberProfile|null>(null),[error,setError]=useState(''),[loading,setLoading]=useState(true),[attempt,setAttempt]=useState(0);
 useEffect(()=>{document.title='ترنم | حساب من'},[]);
 useEffect(()=>{
  const controller=new AbortController();setProfile(null);setError('');setLoading(true);
  getMyProfile(controller.signal).then(result=>{if(!controller.signal.aborted)setProfile(result)}).catch((e:unknown)=>{
   if(controller.signal.aborted)return;
   setError(e instanceof AppointmentApiError&&e.status===403?'دسترسی به اطلاعات حساب مجاز نیست.':e instanceof Error?e.message:'دریافت اطلاعات حساب ناموفق بود.');
  }).finally(()=>{if(!controller.signal.aborted)setLoading(false)});
  return()=>controller.abort();
 },[attempt]);
 return <AppointmentPage title="حساب من" subtitle="اطلاعات و تنظیمات حساب ترنم" activeSection="account"><div className="patient-account">
  <section className="account-summary" aria-label="خلاصهٔ حساب" aria-busy={loading}>
   <img className="account-avatar" src="/assets/account/avatar.svg" width="58" height="58" alt=""/>
   <div className="account-summary-copy">{loading?<p role="status">در حال دریافت حساب…</p>:profile?<><h2>{profile.fullName.trim()||'نام ثبت نشده'}</h2><p>مشخصات اعلامی شما</p></>:<p>اطلاعات حساب دریافت نشد</p>}</div>
   <p className="account-identity-note">شمارهٔ همراه: در انتظار اتصال مرجع هویت</p>
   <button className="account-refresh" disabled={loading} onClick={()=>setAttempt(x=>x+1)}>تازه‌سازی حساب</button>
  </section>
  {error&&<p className="account-error" role="alert">{error}</p>}
  {profile&&!profile.fullName.trim()&&<p className="account-empty" role="status">برای تکمیل نام، وارد اطلاعات فردی شوید.</p>}
  <section className="account-membership"><h2>عضویت و پرداخت</h2><p>حق عضویت بر اساس سن عضو است؛ پرداخت می‌تواند توسط خود عضو یا فرد دیگری انجام شود.</p><span>وضعیت عضویت: در انتظار اتصال</span></section>
  <section className="account-group"><h2>اعضای مجموعهٔ من</h2><p>دعوت خانواده یا هر فرد دلخواه؛ هر شخص ثبت‌نام مستقل دارد.</p></section>
  <h2 className="account-settings-title">تنظیمات حساب</h2>
  <div className="account-settings">
   <a href="/patient-app/personal-info"><span>اطلاعات فردی</span><span aria-hidden="true">‹</span></a>
   <button disabled title="مدیریت رضایت‌ها هنوز به بک‌اند متصل نشده است."><span>رضایت‌ها و دسترسی‌ها</span><small>در انتظار اتصال</small></button>
   <button disabled title="تنظیمات اعلان‌ها و حریم خصوصی هنوز به بک‌اند متصل نشده است."><span>اعلان‌ها و حریم خصوصی</span><small>در انتظار اتصال</small></button>
   <a href="/patient-app/support"><span>پشتیبانی</span><span aria-hidden="true">‹</span></a>
   <button className="account-signout" disabled title="خروج امن با اتصال نشست ورود فعال می‌شود."><span>خروج از حساب</span><small>نیازمند اتصال ورود</small></button>
  </div>
  <a className="account-install-link" href="/patient-app/install">نصب اپ ترنم روی گوشی</a>
 </div></AppointmentPage>;
}
