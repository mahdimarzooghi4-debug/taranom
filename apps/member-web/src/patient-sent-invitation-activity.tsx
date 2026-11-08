import {useEffect,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,getMySentInvitationActivity,revokeMyGroupInvitation,type SentInvitationActivity,type SentInvitationActivityPage,type GroupInvitationState} from './taranom-api';
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
 const [pendingOnly,setPendingOnly]=useState(false);
 const [confirm,setConfirm]=useState<SentInvitationActivity|null>(null);
 const [pendingRevoke,setPendingRevoke]=useState<{draftRef:string;invitationRef:string}|null>(null);
 const [revokeUnknown,setRevokeUnknown]=useState(false),[retryable,setRetryable]=useState(false);
 const [revoking,setRevoking]=useState(false),[revocationNotice,setRevocationNotice]=useState('');
 const [ref]=useState(()=>new URLSearchParams(location.search).get('invitation'));
 const [draftFilter]=useState(()=>new URLSearchParams(location.search).get('draft'));
 const validRef=(v:string)=>/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(v);
 const valid=!(ref!==null&&draftFilter!==null)&&
  (ref===null||validRef(ref))&&(draftFilter===null||validRef(draftFilter));
 useEffect(()=>{document.title='ترنم | پیگیری دعوت‌های ارسالی'},[]);
 useEffect(()=>{
  const c=new AbortController();setData(null);setError('');setLoading(true);
  if(!valid){
   setError(ref!==null&&!validRef(ref)&&draftFilter===null?'شناسهٔ دعوت معتبر نیست.':'شناسهٔ دعوت یا پیش‌نویس معتبر نیست.');setLoading(false);return()=>c.abort();
  }
  getMySentInvitationActivity({
   ...(before?{before}:{}),...(ref?{invitationRef:ref}:{}),
   ...(draftFilter?{draftRef:draftFilter}:{}),
   ...(pendingOnly&&!ref?{state:'Pending' as const}:{})
  },c.signal).then(result=>{if(!c.signal.aborted)setData(result)})
   .catch((e:unknown)=>{if(!c.signal.aborted)setError(
    e instanceof Error?e.message:'دریافت وضعیت دعوت‌ها ناموفق بود.')})
   .finally(()=>{if(!c.signal.aborted)setLoading(false)});
  return()=>c.abort();
 },[before,reload,ref,draftFilter,pendingOnly,valid]);

 function selectFilter(next:boolean){
  if(loading||revoking||revokeUnknown||ref||pendingOnly===next)return;
  setConfirm(null);setData(null);setBefore(undefined);setPendingOnly(next);
 }
 const definite=(e:unknown)=>e instanceof AppointmentApiError&&e.status>=400&&e.status<500;
 async function revoke(mode:'start'|'check'|'retry',candidate?:SentInvitationActivity){
  if(revoking)return;
  const target=mode==='start'&&candidate?{
   draftRef:candidate.draftRef,invitationRef:candidate.invitationRef
  }:pendingRevoke;
  if(!target)return;
  if(mode==='start'&&(candidate?.draftState!=='Draft'||candidate.state!=='Pending'))return;
  if(mode==='retry'&&(!revokeUnknown||!retryable))return;
  setRevoking(true);setData(null);setError('');setConfirm(null);
  setRevocationNotice('');setPendingRevoke(target);setRetryable(false);
  try{
   if(mode==='check'){
    const page=await getMySentInvitationActivity({invitationRef:target.invitationRef});
    const item=page.invitations[0];
    if(!item||item.draftRef!==target.draftRef)
     throw new Error('رسید لغو با حساب و پیش‌نویس شما تطبیق ندارد.');
    if(item.state==='Revoked'){
     setRevocationNotice('غیرفعال‌شدن پیوند دعوت تأیید شد.');
     setPendingRevoke(null);setRevokeUnknown(false);setReload(n=>n+1);
    }else if(item.state==='Pending'&&item.draftState==='Draft'){
     setRevokeUnknown(true);setRetryable(true);
     setError('لغو هنوز ثبت نشده است؛ می‌توانید همان درخواست لغو را دوباره بفرستید.');
    }else{
     setPendingRevoke(null);setRevokeUnknown(false);
     setError('وضعیت دعوت تغییر کرده است و دیگر نمی‌توان آن را لغو کرد.');
     setReload(n=>n+1);
    }
   }else{
    const result=await revokeMyGroupInvitation(target.draftRef,target.invitationRef);
    if(result.state!=='Revoked')throw new Error('لغو پیوند هنوز قابل تأیید نیست.');
    setRevocationNotice('لغو پیوند دعوت ثبت و تأیید شد.');
    setPendingRevoke(null);setRevokeUnknown(false);setReload(n=>n+1);
   }
  }catch(e){
   setError(e instanceof Error?e.message:'نتیجهٔ لغو نامشخص است.');
   if(definite(e)){
    setPendingRevoke(null);setRevokeUnknown(false);
   }else{
    setRevokeUnknown(true);
   }
  }finally{setRevoking(false)}
 }
 return <AppointmentPage title={ref?'جزئیات دعوت ارسالی':draftFilter?'دعوت‌های این پیش‌نویس':'دعوت‌های ارسالی من'}
  subtitle="پیگیری پاسخ‌های ثبت‌شده، نه عضویت فعال" activeSection="account">
  <section className="patient-group-drafts group-invitation-history owner-invitation-activity" aria-busy={loading}>
   {!ref&&<div className="owner-invitation-filters" role="group" aria-label="فیلتر دعوت‌های ارسالی">
    <button type="button" aria-pressed={!pendingOnly} disabled={loading||revoking||revokeUnknown}
     onClick={()=>selectFilter(false)}>همه دعوت‌ها</button>
    <button type="button" aria-pressed={pendingOnly} disabled={loading||revoking||revokeUnknown}
     onClick={()=>selectFilter(true)}>در انتظار پاسخ</button>
   </div>}
   {draftFilter&&<p className="owner-invitation-draft-note">تاریخچهٔ پیوندهای همین پیش‌نویس: <bdi>{draftFilter}</bdi></p>}
   {loading&&<p role="status">در حال دریافت وضعیت دعوت‌ها…</p>}
   {error&&<p role="alert" className="group-draft-error">{error}</p>}
   {revocationNotice&&<p role="status" className="group-draft-notice">{revocationNotice}</p>}
   {revokeUnknown&&<section className="group-revoke-recovery" aria-label="بازیابی نتیجه لغو">
    <p>نتیجهٔ لغو پیوند هنوز قطعی نیست. تا تأیید نتیجه، پیوند قبلی نمایش داده نمی‌شود.</p>
    <button disabled={revoking} onClick={()=>void revoke('check')}>بررسی نتیجهٔ لغو</button>
    {retryable&&<button disabled={revoking} onClick={()=>void revoke('retry')}>تلاش مجدد همان لغو</button>}
   </section>}

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
     {i.draftState==='Draft'&&i.state==='Pending'&&!revokeUnknown&&(
      confirm?.invitationRef===i.invitationRef?
       <div className="group-archive-confirm" aria-label="تأیید لغو پیوند">
        <p>این پیوند در انتظار غیرفعال شود؟ پاسخ پذیرفته‌شده یا ردشده تغییر نمی‌کند.</p>
        <button disabled={revoking} onClick={()=>void revoke('start',i)}>تأیید لغو همین پیوند</button>
        <button disabled={revoking} onClick={()=>setConfirm(null)}>انصراف از لغو</button>
       </div>:
       <button disabled={revoking} onClick={()=>setConfirm(i)}>لغو پیوند در انتظار</button>
     )}

    </article>)}
    {data&&!data.invitations.length&&<p role="status">{ref?
     'این دعوت در حساب شما ثبت نشده است.':pendingOnly?
     'در این بخش پیوندی در انتظار پاسخ وجود ندارد.':draftFilter?
     'برای این پیش‌نویس پیوند دعوتی ثبت نشده است.':'هنوز پیوند دعوتی از این حساب صادر نشده است.'}</p>}
   </div>
   <p className="group-draft-boundary">
    نام نمایشی، فقط متن پیش‌نویس شماست و هویت گیرنده را تأیید نمی‌کند.
    پذیرش دعوت به‌معنای فعال‌سازی عضویت، پرداخت، انتقال کیف پول یا دسترسی درمانی نیست.
   </p>
   <div className="group-draft-pagination">
    <button disabled={loading||!valid||revoking||revokeUnknown} onClick={()=>{
     setBefore(undefined);setReload(n=>n+1);
    }}>تازه‌سازی وضعیت دعوت‌ها</button>
    {data?.nextBefore&&!ref&&<button disabled={loading||revoking||revokeUnknown} onClick={()=>setBefore(data.nextBefore!)}>
     دعوت‌های قدیمی‌تر
    </button>}
   </div>
   {(ref||draftFilter)&&<a className="invitation-history-back" href="/patient-app/invitations-sent">همهٔ دعوت‌های ارسالی</a>}
   <a className="invitation-history-back" href="/patient-app/group-members">پیش‌نویس‌های گروه من</a>
  </section>
 </AppointmentPage>;
}
