import { useEffect, useRef, useState } from 'react';
import { cancelMyAppointment, getMyAppointments, type MemberAppointment } from './taranom-api';
import './figma-appointments.css';
const assets='/assets/appointments/';
const base='/patient-app';
const formatDate=(value:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(new Date(value));
type Filter='future'|'requests'|'past';
type Load={kind:'loading'}|{kind:'error';message:string}|{kind:'ready';items:MemberAppointment[]};
export function FigmaAppointments(){
 const detail=window.location.pathname.endsWith('/appointment-detail');
 const [filter,setFilter]=useState<Filter>(window.location.pathname.endsWith('/request-tracking')?'requests':'future');
 const [load,setLoad]=useState<Load>({kind:'loading'});
 const [refresh,setRefresh]=useState(0);
 const [pending,setPending]=useState<MemberAppointment|null>(null);
 const [saving,setSaving]=useState(false);
 const [mutationError,setMutationError]=useState('');
 const [notice,setNotice]=useState('');
 const [clock,setClock]=useState(()=>Date.now());
 const mounted=useRef(true);
 const requestIds=useRef(new Map<string,string>());
 const trigger=useRef<HTMLButtonElement|null>(null);
 const cancelButton=useRef<HTMLButtonElement>(null);
 const title=detail?'جزئیات نوبت':'نوبت‌های من';
 useEffect(()=>{mounted.current=true;return()=>{mounted.current=false}},[]);
 useEffect(()=>{document.title=`ترنم | ${title}`},[title]);
 useEffect(()=>{
  const controller=new AbortController();setLoad({kind:'loading'});
  getMyAppointments(controller.signal).then(({appointments})=>{
   if(!controller.signal.aborted){setLoad({kind:'ready',items:appointments});setClock(Date.now())}
  }).catch((e:unknown)=>{if(!controller.signal.aborted)setLoad({kind:'error',message:e instanceof Error?e.message:'دریافت نوبت‌ها ناموفق بود.'})});
  return()=>controller.abort();
 },[refresh]);
 useEffect(()=>{
  if(!pending)return;
  cancelButton.current?.focus();
  const key=(e:KeyboardEvent)=>{
   if(e.key==='Escape'&&!saving){e.preventDefault();setPending(null);trigger.current?.focus()}
   if(e.key==='Tab'){
    const buttons=Array.from(document.querySelectorAll<HTMLButtonElement>('.appointment-confirm button:not(:disabled)'));
    if(!buttons.length){e.preventDefault();return}
    if(e.shiftKey&&document.activeElement===buttons[0]){e.preventDefault();buttons.at(-1)?.focus()}
    if(!e.shiftKey&&document.activeElement===buttons.at(-1)){e.preventDefault();buttons[0]?.focus()}
   }
  };document.addEventListener('keydown',key);return()=>document.removeEventListener('keydown',key);
 },[pending,saving]);
 async function confirmCancellation(){
  if(!pending||saving)return;
  const item=pending;setSaving(true);setMutationError('');setNotice('');
  const key=`${item.centerRef}:${item.bookingRef}:${item.scheduleRevision}`;
  const requestId=requestIds.current.get(key)??crypto.randomUUID();requestIds.current.set(key,requestId);
  try{
   await cancelMyAppointment(item,requestId);
   if(!mounted.current)return;
   setPending(null);setNotice('لغو نوبت ثبت شد.');setRefresh(x=>x+1);
  }catch(e){
   if(!mounted.current)return;
   setPending(null);setMutationError((e instanceof Error?e.message:'پاسخ لغو نوبت دریافت نشد.')+' پیش از اقدام دوباره، وضعیت نوبت را تازه‌سازی کنید.');
   setRefresh(x=>x+1);
  }finally{if(mounted.current){setSaving(false);trigger.current?.focus()}}
 }
 const items=load.kind==='ready'?load.items:[];
 const selected=items.find(item=>item.bookingRef===new URLSearchParams(window.location.search).get('booking')&&item.centerRef===new URLSearchParams(window.location.search).get('center'));
 const visible=items.filter(item=>filter==='future'?item.state==='Booked'&&Date.parse(item.startsAt)>=clock:filter==='past'?item.state==='CancelledByMember'||Date.parse(item.startsAt)<clock:false);
 const detailsUrl=(item:MemberAppointment)=>`${base}/appointment-detail?${new URLSearchParams({booking:item.bookingRef,center:item.centerRef})}`;
 function cancelAction(item:MemberAppointment){return item.state==='Booked'&&Date.parse(item.startsAt)>=clock&&<button className="appointment-cancel" disabled={saving} onClick={e=>{trigger.current=e.currentTarget;setPending(item)}}>لغو نوبت</button>}
 return <div className="appointments-surface" dir="rtl"><div className="appointments-phone">
  <div className="appointments-body" inert={Boolean(pending)}>
  <header className="appointments-header"><img className="appointments-logo" src={assets+'logo.png'} alt="ترنم · اکوسیستم سلامت"/>
   <a className="appointments-bell" href={base+'/notifications'} aria-label="اعلان‌ها"><img src={assets+'bell.svg'} width="22" height="22" alt=""/></a>
   <h1>{title}</h1><p>{detail?'اطلاعات نوبت ثبت‌شده':'نوبت‌های آینده و درخواست‌های شما'}</p>
  </header>
  <main className="appointments-main" aria-busy={load.kind==='loading'}>
   {!detail&&<><section className="appointment-new"><h2>برای نوبت جدید</h2><p>پزشک، تخصص یا مرکز درمانی را پیدا کنید</p><a href={base+'/booking'}>شروع نوبت‌گیری</a></section>
    <h2 className="appointments-heading">نوبت‌های من</h2><div className="appointment-filters" role="group" aria-label="فیلتر نوبت‌ها">
     {([['future','آینده'],['requests','درخواست‌ها'],['past','گذشته']] as const).map(([key,label])=><button key={key} aria-pressed={filter===key} onClick={()=>setFilter(key)}>{label}</button>)}
    </div></>}
   {notice&&<p className="appointment-success" role="status">{notice}</p>}
   {mutationError&&<p className="appointment-error" role="alert">{mutationError}</p>}
   {load.kind==='loading'&&<p className="appointment-state" role="status">در حال دریافت نوبت‌ها…</p>}
   {load.kind==='error'&&<p className="appointment-state appointment-error" role="alert">{load.message}</p>}
   {load.kind==='ready'&&(detail?selected?<>
    <p className="appointment-success">{selected.state==='Booked'?'نوبت ثبت شده':'لغوشده توسط شما'}</p>
    <article className="appointment-card"><h2>اطلاعات مراجعه</h2><p>مرجع مرکز: <bdi>{selected.centerRef}</bdi></p><p>مرجع نوبت: <bdi>{selected.bookingRef}</bdi></p>
      <p className="appointment-muted">نام پزشک و نشانی مرکز هنوز از مرجع ارائه‌دهندگان دریافت نمی‌شود.</p></article>
    <article className="appointment-card"><small>روز و ساعت</small><time dateTime={selected.startsAt}>{formatDate(selected.startsAt)}</time><hr/><p>مرجع بیمار: <bdi>{selected.memberRef}</bdi></p></article>
    <section className="appointment-record"><h2>نسخه و سوابق این مراجعه</h2><p>نسخهٔ این مراجعه پس از ثبت در پروندهٔ ترنم بیمار نگهداری می‌شود.</p></section>
    <a className="appointment-primary" href={base+'/slots?'+new URLSearchParams({center:selected.centerRef})}>نوبت مجدد در همین مرکز</a>{cancelAction(selected)}<a className="appointment-primary" href={base+'/appointments'}>بازگشت به نوبت‌های من</a>
   </>:<p className="appointment-state" role="status">نوبت موردنظر در حساب شما پیدا نشد.</p>
   :filter==='requests'?<section className="appointment-state"><h2>درخواست‌های در جریان</h2><p>سرویس درخواست‌های در انتظار پاسخ مرکز هنوز متصل نشده است.</p></section>
   :visible.length?visible.map(item=><article className="appointment-card" key={item.centerRef+item.bookingRef}>
    <span className={'appointment-status '+(item.state==='Booked'?'':'cancelled')}>{item.state==='Booked'?'ثبت‌شده':'لغوشده توسط شما'}</span>
    <h3>نوبت مراجعه</h3><p className="appointment-muted">مرجع نوبت: <bdi>{item.bookingRef}</bdi></p><hr/>
    <p>مرجع مرکز: <bdi>{item.centerRef}</bdi></p><time dateTime={item.startsAt}>{formatDate(item.startsAt)}</time>
    <a className="appointment-primary" href={detailsUrl(item)}>مشاهدهٔ جزئیات نوبت</a>{cancelAction(item)}
   </article>):<p className="appointment-state" role="status">{filter==='future'?'نوبت آینده‌ای برای شما ثبت نشده است.':'نوبت گذشته یا لغوشده‌ای ندارید.'}</p>)}
   <button className="appointment-refresh" disabled={load.kind==='loading'||saving} onClick={()=>setRefresh(x=>x+1)}>تازه‌سازی نوبت‌ها</button>
  </main>
  <nav className="appointments-nav" aria-label="ناوبری اصلی">
   {([['home','خانه',''],['record','پرونده','/records'],['calendar','نوبت‌ها','/appointments'],['account','حساب من','/account']] as const).map(([icon,label,path])=><a href={base+path} key={icon} aria-current={icon==='calendar'?'page':undefined}><img src={assets+icon+'.svg'} width="24" height="24" alt=""/>{label}</a>)}
  </nav></div>
  {pending&&<div className="appointment-modal"><section className="appointment-confirm" role="dialog" aria-modal="true" aria-labelledby="cancel-title"><h2 id="cancel-title">لغو این نوبت؟</h2><p>{formatDate(pending.startsAt)}</p><button ref={cancelButton} disabled={saving} onClick={()=>{setPending(null);trigger.current?.focus()}}>بازگشت</button><button disabled={saving} onClick={()=>void confirmCancellation()}>{saving?'در حال لغو…':'تأیید لغو نوبت'}</button></section></div>}
 </div></div>;
}
