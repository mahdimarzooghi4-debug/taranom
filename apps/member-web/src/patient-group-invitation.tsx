import {useEffect,useRef,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,getMyGroupDrafts,getMyGroupInvitation,issueMyGroupInvitation,inspectGroupInvitation,type GroupDraft,type GroupInvitation} from './taranom-api';
import './patient-group-drafts.css';
const states={Pending:'منتظر پاسخ',Accepted:'دعوت پذیرفته شد',Declined:'دعوت رد شد',Revoked:'پیوند لغو شده',Expired:'پیوند منقضی شده'};
const validRef=(s:string)=>/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(s);
const definite=(e:unknown)=>e instanceof AppointmentApiError&&e.status>=400&&e.status<500;
const expiry=(v:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeZone:'Asia/Tehran'}).format(new Date(v));
export function PatientGroupInvitation({receiving=false}:{receiving?:boolean}){
 const [draft,setDraft]=useState<GroupDraft|null>(null),[invitation,setInvitation]=useState<GroupInvitation|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false),[consent,setConsent]=useState(false),[link,setLink]=useState(''),[uncertain,setUncertain]=useState(false),[loaded,setLoaded]=useState(false);
 const draftRef=useRef(new URLSearchParams(location.search).get('draft')??''),token=useRef(receiving?location.hash.slice(1):''),pending=useRef<{requestId:string;token:string}|null>(null),decision=useRef<'Accepted'|'Declined'|null>(null),alive=useRef(true),lock=useRef(false);
 useEffect(()=>{alive.current=true;document.title='ترنم | دعوت گروه';if(receiving){history.replaceState(null,'',location.pathname);setLoaded(true)}else{const c=new AbortController();if(!validRef(draftRef.current)){setError('شناسهٔ پیش‌نویس معتبر نیست.');setLoaded(true)}else{Promise.all([getMyGroupDrafts({draftRef:draftRef.current},c.signal),getMyGroupInvitation(draftRef.current,undefined,c.signal)]).then(([d,i])=>{if(c.signal.aborted)return;const row=d.drafts.find(v=>v.draftRef===draftRef.current);if(!row)throw new Error('پیش‌نویس در حساب شما پیدا نشد.');setDraft(row);setInvitation(i)}).catch((e:unknown)=>{if(!c.signal.aborted)setError(e instanceof Error?e.message:'دریافت دعوت ناموفق بود.')}).finally(()=>{if(!c.signal.aborted)setLoaded(true)})}return()=>{alive.current=false;c.abort()}}return()=>{alive.current=false}},[receiving]);
 function show(v:GroupInvitation,secret?:string){if(!alive.current)return;setInvitation(v);setError('');setUncertain(false);if(secret&&v.state==='Pending')setLink(location.origin+'/patient-app/join-group#'+secret);else setLink('')}
 async function run(action:'issue'|'preview'|'Accepted'|'Declined'|'check'){
  if(lock.current)return;lock.current=true;setBusy(true);setError('');setLink('');
  try{
   if(action==='issue'){
    const bytes=crypto.getRandomValues(new Uint8Array(32));const c={requestId:crypto.randomUUID(),token:Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('')};pending.current=c;
    show(await issueMyGroupInvitation(draftRef.current,c),c.token);pending.current=null;
   }else if(action==='check'&&!receiving){
    const c=pending.current;if(!c)throw new Error('اطلاعات پیوند در این صفحه موجود نیست.');const v=await getMyGroupInvitation(draftRef.current,c.requestId);if(!v)throw new Error('نتیجه هنوز ثبت نشده است؛ دوباره بررسی کنید.');show(v,c.token);pending.current=null;
   }else{
    if(!/^[a-f0-9]{64}$/.test(token.current))throw new Error('پیوند دعوت کامل یا معتبر نیست.');
    const d=action==='Accepted'||action==='Declined'?action:undefined;if(d)decision.current=d;
    const v=await inspectGroupInvitation(token.current,d);show(v);if(action==='check'&&v.state==='Pending'){setUncertain(true);setError('پاسخ هنوز تأیید نشده است؛ دوباره بررسی کنید.')}else decision.current=null;
   }
  }catch(e){if(alive.current){setInvitation(null);setError(e instanceof Error?e.message:'دریافت نتیجه ناموفق بود.');const unknown=!definite(e);setUncertain((action==='issue'||Boolean(pending.current)||Boolean(decision.current))&&unknown);if(!unknown){pending.current=null;decision.current=null}}}
  finally{lock.current=false;if(alive.current)setBusy(false)}
 }
 return <AppointmentPage title={receiving?'دعوت پیوستن به گروه':'پیوند دعوت گروه'} subtitle={receiving?'پاسخ مستقل در حساب شما':'اشتراک پیوند با فرد مورد نظر'} activeSection="account"><section className="patient-group-drafts group-invitation-page">
 {error&&<p role="alert" className="group-draft-error">{error}</p>}{!loaded&&<p role="status">در حال دریافت…</p>}
 <p className="group-draft-boundary">پذیرش دعوت فقط رضایت برای پیوستن به گروه است. فعال‌سازی عضویت و پرداخت جداگانه انجام می‌شود؛ دسترسی به پروندهٔ سلامت یا کیف پول ایجاد نمی‌شود.</p>
 {draft&&<h2>{draft.recipientLabel}</h2>}
 {invitation&&<section className="group-draft-receipt" aria-label="وضعیت دعوت"><strong>{states[invitation.state]}</strong><p>شناسهٔ دعوت: <bdi>{invitation.invitationRef}</bdi></p><p>اعتبار پیوند تا {expiry(invitation.expiresAt)}</p>{invitation.state==='Accepted'&&<p>عضویت ترنم هنوز از طریق این دعوت فعال نشده است.</p>}</section>}
 {receiving?<>{loaded&&!invitation&&!uncertain&&<button className="group-draft-primary" disabled={busy} onClick={()=>void run('preview')}>بررسی دعوت</button>}{invitation?.state==='Pending'&&!uncertain&&<><label className="group-invitation-consent"><input type="checkbox" checked={consent} disabled={busy} onChange={e=>setConsent(e.target.checked)}/>می‌خواهم به گروه این دعوت بپیوندم و توضیحات بالا را خوانده‌ام.</label><button className="group-draft-primary" disabled={busy||!consent} onClick={()=>void run('Accepted')}>پذیرش دعوت</button><button className="group-invitation-secondary" disabled={busy} onClick={()=>void run('Declined')}>رد دعوت</button></>}</>:<>{draft?.state==='Draft'&&invitation?.state!=='Accepted'&&!uncertain&&<><p>پیوند را فقط برای فرد مورد نظر بفرستید. ساخت پیوند تازه، پیوند قبلیِ بی‌پاسخ را لغو می‌کند. اعتبار هر پیوند هفت روز است.</p><button className="group-draft-primary" disabled={busy} onClick={()=>void run('issue')}>{link?'ساخت پیوند جایگزین':'ساخت پیوند دعوت'}</button></>}{link&&<label className="group-invitation-link">پیوند دعوت<textarea readOnly value={link} dir="ltr" onFocus={e=>e.target.select()}/><span>پیوند را کپی کنید و برای خود فرد بفرستید.</span></label>}{draft?.state==='Withdrawn'&&<p>پیش‌نویس بایگانی شده است؛ پیوند بی‌پاسخ قابل استفاده نیست.</p>}<a href="/patient-app/group-members">بازگشت به پیش‌نویس‌ها</a></>}
 {uncertain&&<button className="group-draft-primary" disabled={busy} onClick={()=>void run('check')}>بررسی نتیجهٔ دعوت</button>}
 </section></AppointmentPage>;
}
