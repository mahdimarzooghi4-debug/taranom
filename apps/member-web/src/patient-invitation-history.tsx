import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {getMyInvitationHistory,type GroupInvitationHistoryPage} from './taranom-api';
import './patient-group-drafts.css';
const date=(value:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(new Date(value));
export function PatientInvitationHistory(){
 const [data,setData]=useState<GroupInvitationHistoryPage|null>(null),[error,setError]=useState(''),[loading,setLoading]=useState(true),[cursor,setCursor]=useState<string|undefined>(),[attempt,setAttempt]=useState(0);
 const [reference]=useState(()=>new URLSearchParams(location.search).get('invitation'));
 const valid=reference===null||/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(reference);
 useEffect(()=>{document.title='ترنم | سابقهٔ پاسخ به دعوت‌ها'},[]);
 useEffect(()=>{const c=new AbortController();setData(null);setError('');setLoading(true);
  if(!valid){setError('شناسهٔ دعوت معتبر نیست.');setLoading(false);return()=>c.abort()}
  getMyInvitationHistory({...cursor?{before:cursor}:{},...reference?{invitationRef:reference}:{}},c.signal).then(r=>{if(!c.signal.aborted)setData(r)}).catch((e:unknown)=>{if(!c.signal.aborted)setError(e instanceof Error?e.message:'دریافت سابقهٔ دعوت‌ها ناموفق بود.')}).finally(()=>{if(!c.signal.aborted)setLoading(false)});return()=>c.abort();
 },[attempt,cursor,reference,valid]);
 return <AppointmentPage title={reference?'پاسخ به دعوت':'سابقهٔ دعوت‌ها'} subtitle="پاسخ‌های ثبت‌شده در حساب شما" activeSection="account"><section className="patient-group-drafts group-invitation-history" aria-busy={loading}>
 {error&&<p role="alert" className="group-draft-error">{error}</p>}{loading&&<p role="status">در حال دریافت سابقهٔ دعوت‌ها…</p>}
 <div className="group-drafts-history">{data?.invitations.map(i=><article className="group-draft-card" key={i.invitationRef}><h2>{i.state==='Accepted'?'دعوت پذیرفته‌شده':'دعوت ردشده'}</h2><p>پاسخ شما: {date(i.respondedAt!)}</p><p className="invitation-history-reference">شناسهٔ دعوت: <bdi>{i.invitationRef}</bdi></p>{reference?<p>زمان ساخت دعوت: {date(i.createdAt)}</p>:<a href={'/patient-app/invitation-history?invitation='+encodeURIComponent(i.invitationRef)}>مشاهدهٔ پاسخ ثبت‌شده</a>}</article>)}{data&&!data.invitations.length&&<p role="status">{reference?'پاسخی برای این دعوت در حساب شما پیدا نشد.':'هنوز پاسخی به دعوت گروه ثبت نکرده‌اید.'}</p>}</div>
 <p className="group-draft-boundary">این فهرست فقط پذیرش یا رد دعوت را نشان می‌دهد؛ وضعیت عضویت، پرداخت یا دسترسی به پروندهٔ سلامت و کیف پول از آن تعیین نمی‌شود.</p>
 <div className="group-draft-pagination"><button disabled={loading||!valid} onClick={()=>{setCursor(undefined);setAttempt(n=>n+1)}}>تازه‌سازی سابقهٔ دعوت‌ها</button>{data?.nextBefore&&!reference&&<button disabled={loading} onClick={()=>setCursor(data.nextBefore!)}>پاسخ‌های قدیمی‌تر</button>}</div>
 {reference&&<a className="invitation-history-back" href="/patient-app/invitation-history">همهٔ پاسخ‌های من</a>}
 </section></AppointmentPage>;
}
