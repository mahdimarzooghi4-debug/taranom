import {useEffect,useRef,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import './patient-install.css';
interface InstallPrompt extends Event{prompt:()=>Promise<void>;userChoice:Promise<{outcome:'accepted'|'dismissed'}>}
export function PatientInstall(){
 const [available,setAvailable]=useState(false),[busy,setBusy]=useState(false),[standalone,setStandalone]=useState(false),[installed,setInstalled]=useState(false),[notice,setNotice]=useState(''),[offline,setOffline]=useState(!navigator.onLine);
 const deferred=useRef<InstallPrompt|null>(null),alive=useRef(true),inFlight=useRef(false),completed=useRef(false);
 useEffect(()=>{
  alive.current=true;document.title='ترنم | نصب اپ';const media=matchMedia('(display-mode: standalone)');setStandalone(media.matches);
  const mode=()=>setStandalone(media.matches),network=()=>setOffline(!navigator.onLine);
  const offer=(event:Event)=>{event.preventDefault();deferred.current=event as InstallPrompt;setAvailable(true)};
  const complete=()=>{completed.current=true;deferred.current=null;setAvailable(false);setInstalled(true);setNotice('نصب ترنم در مرورگر تأیید شد.')};
  window.addEventListener('beforeinstallprompt',offer);window.addEventListener('appinstalled',complete);window.addEventListener('online',network);window.addEventListener('offline',network);media.addEventListener('change',mode);
  return()=>{alive.current=false;deferred.current=null;window.removeEventListener('beforeinstallprompt',offer);window.removeEventListener('appinstalled',complete);window.removeEventListener('online',network);window.removeEventListener('offline',network);media.removeEventListener('change',mode)};
 },[]);
 async function install(){
  const event=deferred.current;if(!event||inFlight.current||offline)return;
  inFlight.current=true;deferred.current=null;setBusy(true);setAvailable(false);setNotice('');
  try{await event.prompt();const choice=await event.userChoice;if(alive.current&&!completed.current)setNotice(choice.outcome==='accepted'?'درخواست نصب به مرورگر ارسال شد.':'نصب انجام نشد؛ می‌توانید بعداً از منوی مرورگر اقدام کنید.')}
  catch{if(alive.current&&!completed.current)setNotice('پنجرهٔ نصب باز نشد. از منوی مرورگر برای نصب اقدام کنید.')}
  finally{inFlight.current=false;if(alive.current)setBusy(false)}
 }
 return <AppointmentPage title="نصب اپ ترنم" subtitle="دسترسی سریع‌تر از صفحهٔ اصلی گوشی" activeSection="account"><section className="patient-install">
  <section className="install-intro"><img src="/assets/install/icon-192.png" width="96" height="96" alt="ترنم"/><h2>ترنم، کنار شما</h2><p>ترنم را به صفحهٔ اصلی گوشی اضافه کنید تا حساب، نوبت‌ها و پشتیبانی را آسان‌تر باز کنید.</p></section>
  {standalone&&<p role="status">اکنون ترنم را در پنجرهٔ مستقل اپ باز کرده‌اید.</p>}
  {notice&&<p role="status">{notice}</p>}
  {offline&&<p role="alert">اتصال اینترنت برقرار نیست. برای نصب و استفاده از خدمات، اینترنت را وصل کنید.</p>}
  {!standalone&&!installed&&available&&<button className="install-primary" disabled={busy||offline} onClick={()=>void install()}>نصب ترنم</button>}
  {busy&&<p role="status">در حال باز کردن پنجرهٔ نصب…</p>}
  {!standalone&&!installed&&<section className="install-guide"><h2>نصب از منوی مرورگر</h2><ol><li>منوی مرورگر گوشی را باز کنید.</li><li>گزینهٔ «نصب برنامه» یا «افزودن به صفحهٔ اصلی» را انتخاب کنید.</li><li>افزودن ترنم را در پنجرهٔ مرورگر تأیید کنید.</li></ol><p>اگر گزینهٔ نصب نمایش داده نشد، همچنان می‌توانید از ترنم در مرورگر استفاده کنید.</p></section>}
  <p className="install-network-note">خدمات حساب و نوبت‌دهی به اینترنت نیاز دارند. نصب اپ به‌معنی فعال‌شدن عضویت یا تکمیل اطلاعات حساب نیست.</p>
  <a className="install-account-link" href="/patient-app/account">بازگشت به حساب من</a>
 </section></AppointmentPage>;
}
