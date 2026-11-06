import {useEffect,useRef,useState} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,getMyNotifications,markMyNotificationRead,type NotificationPage} from './taranom-api';
import './patient-notifications.css';
const when=(value:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(new Date(value));
export function PatientNotifications(){
 const [data,setData]=useState<NotificationPage|null>(null),[error,setError]=useState(''),[loading,setLoading]=useState(true),[cursor,setCursor]=useState<string|undefined>(),[attempt,setAttempt]=useState(0),[saving,setSaving]=useState(false),[uncertain,setUncertain]=useState(false),[notice,setNotice]=useState('');
 const pending=useRef<string|null>(null),busy=useRef(false),alive=useRef(true);
 useEffect(()=>{alive.current=true;document.title='ترنم | اعلان‌ها';return()=>{alive.current=false}},[]);
 useEffect(()=>{const c=new AbortController();setData(null);setError('');setLoading(true);getMyNotifications(cursor?{before:cursor}:{},c.signal).then(result=>{if(!c.signal.aborted)setData(result)}).catch((e:unknown)=>{if(!c.signal.aborted)setError(e instanceof AppointmentApiError&&e.status===403?'دسترسی به اعلان‌های حساب مجاز نیست.':e instanceof Error?e.message:'دریافت اعلان‌ها ناموفق بود.')}).finally(()=>{if(!c.signal.aborted)setLoading(false)});return()=>c.abort()},[cursor,attempt]);
 function confirmed(){if(!alive.current)return;pending.current=null;setUncertain(false);setNotice('خواندن اعلان ثبت شد.');setAttempt(x=>x+1)}
 async function mark(ref:string){
  if(busy.current||uncertain)return;busy.current=true;pending.current=ref;setSaving(true);setError('');setNotice('');
  try{await markMyNotificationRead(ref);confirmed()}
  catch(e){if(alive.current){setError(e instanceof Error?e.message:'نتیجهٔ خواندن دریافت نشد.');if(e instanceof AppointmentApiError&&e.status>=400&&e.status<500){pending.current=null;setData(null);setUncertain(false)}else setUncertain(true)}}
  finally{busy.current=false;if(alive.current)setSaving(false)}
 }
 async function reconcile(){
  if(busy.current||!pending.current)return;const ref=pending.current;busy.current=true;setSaving(true);
  try{const page=await getMyNotifications({notificationRef:ref});if(page.notifications.some(n=>n.notificationRef===ref&&n.readAt))confirmed();else if(alive.current)setError('خواندن اعلان هنوز تأیید نشده است؛ دوباره نتیجه را بررسی کنید.')}
  catch{if(alive.current){setData(null);setError('دریافت نتیجه ناموفق بود. دوباره بررسی کنید.')}}finally{busy.current=false;if(alive.current)setSaving(false)}
 }
 return <AppointmentPage title="اعلان‌ها" subtitle="خبرهای حساب، عضویت و خدمات ترنم" activeSection="home" backHref="/patient-app/account"><section className="patient-notifications">
  <div className="notifications-heading"><h2>جدیدترین اعلان‌ها</h2>{data&&<p className="notifications-count">{new Intl.NumberFormat('fa-IR').format(data.unreadCount)} خوانده‌نشده</p>}</div>{loading&&<p role="status">در حال دریافت اعلان‌ها…</p>}{error&&<p className="notifications-error" role="alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
  {data&&<>{data.notifications.map(item=><article className="notification-item" key={item.notificationRef}>
   <small>{!item.readAt&&<img src="/assets/notifications/unread.svg" width="8" height="8" alt="خوانده‌نشده"/>}پشتیبانی حساب</small><h3>پاسخ پشتیبانی ثبت شد</h3><p>برای مشاهدهٔ پاسخ درخواست خود، وارد پشتیبانی شوید.</p>
   <div className="notification-actions"><time dateTime={item.createdAt}>{when(item.createdAt)}</time><a href={'/patient-app/support?'+new URLSearchParams({requestId:item.sourceRequestId})}>مشاهدهٔ پاسخ</a>{item.readAt?<span>خوانده‌شده</span>:<button disabled={saving||uncertain} onClick={()=>void mark(item.notificationRef)}>خواندم</button>}</div>
  </article>)}{!data.notifications.length&&<p className="notifications-empty" role="status">اعلانی برای نمایش وجود ندارد.</p>}</>}
  {uncertain?<button className="notifications-refresh" disabled={saving} onClick={()=>void reconcile()}>بررسی نتیجهٔ خواندن</button>:<div className="notifications-paging"><button disabled={loading||saving} onClick={()=>{setCursor(undefined);setAttempt(x=>x+1);setNotice('')}}>تازه‌سازی اعلان‌ها</button>{data?.nextBefore&&<button disabled={loading||saving} onClick={()=>{setCursor(data.nextBefore!);setNotice('')}}>اعلان‌های قدیمی‌تر</button>}</div>}
  <p className="notifications-privacy">اعلان‌ها فقط برای حساب شما نمایش داده می‌شوند. در حال حاضر پاسخ‌های ثبت‌شدهٔ پشتیبانی در این بخش قابل مشاهده‌اند.</p>
 </section></AppointmentPage>;
}
