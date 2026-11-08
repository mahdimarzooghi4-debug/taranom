import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {getMySentInvitationActivity,type SentInvitationActivityPage,type GroupInvitationState} from './taranom-api';
import './patient-group-drafts.css';

const status:Record<GroupInvitationState,string>={
 Pending:'منتظر پاسخ',Accepted:'پاسخ: پذیرفته شد',Declined:'پاسخ: رد شد',
 Revoked:'پیوند لغو شده',Expired:'پیوند منقضی شده'
};
const when=(value:string)=>new Intl.DateTimeFormat('fa-IR',{
 dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'
}).format(new Date(value));

export function PatientSentInvitationActivity(){
 const [data,setData]=useState<SentInvitationActivityPage|null>(null);
 const [error,setError]=useState(''),[loading,setLoading]=useState(true);
 const [before,setBefore]=useState<string|undefined>(),[reload,setReload]=useState(0);
 const [ref]=useState(()=>new URLSearchParams(location.search).get('invitation'));
 const valid=ref===null||/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(ref);
 useEffect(()=>{document.title='ترنم | پیگیری دعوت‌های ارسالی'},[]);
 useEffect(()=>{
  const c=new AbortController();setData(null);setError('');setLoading(true);
  if(!valid){
   setError('شناسهٔ دعوت معتبر نیست.');setLoading(false);return()=>c.abort();
  }
  getMySentInvitationActivity({
   ...(before?{before}:{}),...(ref?{invitationRef:ref}:{})
  },c.signal).then(result=>{if(!c.signal.aborted)setData(result)})
   .catch((e:unknown)=>{if(!c.signal.aborted)setError(
    e instanceof Error?e.message:'دریافت وضعیت دعوت‌ها ناموفق بود.')})
   .finally(()=>{if(!c.signal.aborted)setLoading(false)});
  return()=>c.abort();
 },[before,reload,ref,valid]);
 return <AppointmentPage title={ref?'جزئیات دعوت ارسالی':'دعوت‌های ارسالی من'}
  subtitle="پیگیری پاسخ‌های ثبت‌شده، نه عضویت فعال" activeSection="account">
  <section className="patient-group-drafts group-invitation-history owner-invitation-activity" aria-busy={loading}>
   {loading&&<p role="status">در حال دریافت وضعیت دعوت‌ها…</p>}
   {error&&<p role="alert" className="group-draft-error">{error}</p>}
   <div className="group-drafts-history">
    {data?.invitations.map(i=><article className="group-draft-card" key={i.invitationRef}>
     <h2>{i.recipientLabel}</h2>
     <p><strong>{status[i.state]}</strong></p>
     <p>ساخت پیوند: {when(i.createdAt)}</p>
     <p>پایان اعتبار: {when(i.expiresAt)}</p>
     {i.respondedAt&&<p>زمان پاسخ مستقل: {when(i.respondedAt)}</p>}
     {i.draftState==='Withdrawn'&&<p>پیش‌نویس دعوت بایگانی شده است.</p>}
     <p className="invitation-history-reference">شناسهٔ دعوت: <bdi>{i.invitationRef}</bdi></p>
     {ref===null?<a href={'/patient-app/invitations-sent?invitation='+encodeURIComponent(i.invitationRef)}>جزئیات دعوت ثبت‌شده</a>:null}
     {i.draftState==='Draft'&&<a href={'/patient-app/group-invitation?draft='+encodeURIComponent(i.draftRef)}>مدیریت پیوند همان پیش‌نویس</a>}
    </article>)}
    {data&&!data.invitations.length&&<p role="status">{ref?
     'این دعوت در حساب شما ثبت نشده است.':'هنوز پیوند دعوتی از این حساب صادر نشده است.'}</p>}
   </div>
   <p className="group-draft-boundary">
    نام نمایشی، فقط متن پیش‌نویس شماست و هویت گیرنده را تأیید نمی‌کند.
    پذیرش دعوت به‌معنای فعال‌سازی عضویت، پرداخت، انتقال کیف پول یا دسترسی درمانی نیست.
   </p>
   <div className="group-draft-pagination">
    <button disabled={loading||!valid} onClick={()=>{
     setBefore(undefined);setReload(n=>n+1);
    }}>تازه‌سازی وضعیت دعوت‌ها</button>
    {data?.nextBefore&&!ref&&<button disabled={loading} onClick={()=>setBefore(data.nextBefore!)}>
     دعوت‌های قدیمی‌تر
    </button>}
   </div>
   {ref&&<a className="invitation-history-back" href="/patient-app/invitations-sent">همهٔ دعوت‌های ارسالی</a>}
   <a className="invitation-history-back" href="/patient-app/group-members">پیش‌نویس‌های گروه من</a>
  </section>
 </AppointmentPage>;
}
