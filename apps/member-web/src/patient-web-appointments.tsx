import {useEffect,useRef,useState} from 'react';
import {cancelMyAppointment,getMyAppointments,type MemberAppointment} from './taranom-api';
import './patient-web-appointments.css';
type Load={kind:'loading'}|{kind:'error';message:string}|{kind:'ready';items:MemberAppointment[]};
const date=(value:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(new Date(value));
export function PatientWebAppointments(){
 const [load,setLoad]=useState<Load>({kind:'loading'}),[refresh,setRefresh]=useState(0);
 const [pending,setPending]=useState<string|null>(null),[saving,setSaving]=useState(false),[notice,setNotice]=useState(''),[error,setError]=useState('');
 const [now,setNow]=useState(()=>Date.now());
 const alive=useRef(true),busy=useRef(false),requestIds=useRef(new Map<string,string>());
 useEffect(()=>{alive.current=true;return()=>{alive.current=false}},[]);
 useEffect(()=>{
  const controller=new AbortController();setLoad({kind:'loading'});setPending(null);
  getMyAppointments(controller.signal).then(({appointments})=>{if(!controller.signal.aborted){setLoad({kind:'ready',items:appointments});setNow(Date.now())}}).catch((e:unknown)=>{if(!controller.signal.aborted)setLoad({kind:'error',message:e instanceof Error?e.message:'دریافت نوبت‌ها ناموفق بود.'})});
  return()=>controller.abort();
 },[refresh]);
 async function cancel(item:MemberAppointment){
  if(busy.current)return;
  busy.current=true;setSaving(true);setNotice('');setError('');
  const key=item.centerRef+':'+item.bookingRef+':'+item.scheduleRevision;
  const requestId=requestIds.current.get(key)??crypto.randomUUID();requestIds.current.set(key,requestId);
  try{await cancelMyAppointment(item,requestId);if(alive.current)setNotice('لغو نوبت ثبت شد.')}
  catch(e){if(alive.current)setError((e instanceof Error?e.message:'پاسخ لغو دریافت نشد.')+' وضعیت نوبت دوباره از سامانه دریافت می‌شود.')}
  finally{busy.current=false;if(alive.current){setSaving(false);setPending(null);setRefresh(x=>x+1)}}
 }
 const items=load.kind==='ready'?load.items:[];
 const future=items.filter(item=>item.state==='Booked'&&Date.parse(item.startsAt)>=now);
 const history=items.filter(item=>item.state==='CancelledByMember'||Date.parse(item.startsAt)<now);
 const count=(n:number)=>load.kind==='ready'?n.toLocaleString('fa-IR'):'—';
 return <section className="portal-page web-live-appointments" aria-labelledby="portal-page-title" aria-busy={load.kind==='loading'}>
  <header className="portal-page-heading"><div><h1 id="portal-page-title" tabIndex={-1}>نوبت‌های من</h1><p>نوبت‌های ثبت‌شدهٔ خود را پیگیری کنید.</p></div><button type="button" disabled title="جست‌وجوی مراکز هنوز متصل نشده است">گرفتن نوبت جدید</button></header>
  <section className="portal-search-card" aria-label="جست‌وجوی پزشک یا مرکز درمانی"><strong>جست‌وجوی پزشک یا مرکز درمانی</strong><div className="portal-search-fields"><label>شهر<input disabled placeholder="پس از اتصال فهرست مراکز"/></label><label>تخصص<input disabled placeholder="پس از اتصال فهرست پزشکان"/></label><button type="button" disabled>جست‌وجو</button></div></section>
  <div className="portal-page-subtitle"><h2>وضعیت نوبت‌ها</h2><button type="button" disabled={load.kind==='loading'||saving} onClick={()=>{setError('');setNotice('');setRefresh(x=>x+1)}}>تازه‌سازی نوبت‌ها</button></div>
  <div className="appointment-stats"><article><small>نوبت‌های گذشته</small><strong>{count(items.filter(i=>i.state==='Booked'&&Date.parse(i.startsAt)<now).length)}</strong><span>تاریخ گذشته؛ وضعیت مراجعه تأیید نشده</span></article><article><small>در انتظار پاسخ</small><strong>—</strong><span>سرویس درخواست‌ها متصل نیست</span></article><article><small>نوبت‌های آینده</small><strong>{count(future.length)}</strong><span>در انتظار مراجعه</span></article></div>
  {notice&&<p className="portal-info-note" role="status">{notice}</p>}{error&&<p className="portal-warning" role="alert">{error}</p>}
  {load.kind==='loading'&&<p role="status" className="portal-info-note">در حال دریافت نوبت‌ها…</p>}
  {load.kind==='error'&&<p role="alert" className="portal-warning">{load.message}</p>}
  {load.kind==='ready'&&<>
  <section className="portal-list-section"><h2>نوبت‌های پیش رو</h2>{future.length?future.map(item=><article className="web-live-appointment-card" key={item.centerRef+item.bookingRef}>
    <div className="appointment-card-main"><span className="sample-status">ثبت‌شده</span><h3>نوبت مراجعه</h3><p>مرجع مرکز: <bdi>{item.centerRef}</bdi></p><strong><time dateTime={item.startsAt}>{date(item.startsAt)}</time></strong><small>نام پزشک و نشانی مرکز هنوز از مرجع ارائه‌دهندگان دریافت نمی‌شود.</small></div>
    <div className="appointment-card-actions"><details><summary>جزئیات نوبت</summary><p>مرجع نوبت: <bdi>{item.bookingRef}</bdi></p><p>مرجع بیمار: <bdi>{item.memberRef}</bdi></p></details>
    <a href={'/patient-app/slots?'+new URLSearchParams({center:item.centerRef})}>نوبت مجدد در همین مرکز</a>
    {pending===item.bookingRef?<div className="web-cancel-confirm" role="group" aria-label="تأیید لغو نوبت"><p>این نوبت لغو شود؟</p><button type="button" disabled={saving} onClick={()=>void cancel(item)}>{saving?'در حال لغو…':'تأیید لغو نوبت'}</button><button type="button" disabled={saving} onClick={()=>setPending(null)}>بازگشت</button></div>:<button type="button" disabled={saving} onClick={()=>setPending(item.bookingRef)}>لغو نوبت</button>}</div>
  </article>):<p className="portal-info-note" role="status">نوبت آینده‌ای برای شما ثبت نشده است.</p>}</section>
  <section className="portal-list-section"><h2>در انتظار پاسخ مرکز</h2><p className="portal-info-note">سرویس درخواست‌های در انتظار پاسخ مرکز هنوز متصل نشده است.</p></section>
  <section className="portal-list-section"><h2>سوابق نوبت</h2>{history.length?<div className="portal-table">{history.map(item=><div key={item.centerRef+item.bookingRef}><time dateTime={item.startsAt}>{date(item.startsAt)}</time><strong>{item.state==='CancelledByMember'?'لغوشده توسط شما':'زمان مراجعه گذشته است'}</strong><small><bdi>{item.centerRef}</bdi></small><details><summary>جزئیات نوبت</summary><p><bdi>{item.bookingRef}</bdi></p><a href={'/patient-app/slots?'+new URLSearchParams({center:item.centerRef})}>نوبت مجدد در همین مرکز</a></details></div>)}</div>:<p className="portal-info-note">سابقهٔ نوبتی برای نمایش وجود ندارد.</p>}</section>
  </>}
 </section>;
}
