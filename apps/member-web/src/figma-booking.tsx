import {useEffect,useRef,useState,type ReactNode} from 'react';
import {AppointmentApiError,bookMemberAppointment,getMemberSchedule,getMemberScopes,getMyAppointments,type MemberSchedule,type OpenSlot} from './taranom-api';
import './figma-appointments.css';
import './figma-booking.css';
const base='/patient-app';
const date=(value:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(new Date(value));
const dayKey=(value:string)=>new Intl.DateTimeFormat('en-CA',{timeZone:'Asia/Tehran',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date(value));
const dayLabel=(value:string)=>new Intl.DateTimeFormat('fa-IR',{timeZone:'Asia/Tehran',weekday:'short',month:'short',day:'numeric'}).format(new Date(value));
const time=(value:string)=>new Intl.DateTimeFormat('fa-IR',{timeZone:'Asia/Tehran',hour:'2-digit',minute:'2-digit'}).format(new Date(value));
export function AppointmentPage({title,subtitle,children,activeSection='appointments'}:{title:string;subtitle:string;children:ReactNode;activeSection?:'appointments'|'account'}){
 return <div className="appointments-surface" dir="rtl"><div className="appointments-phone"><div className="appointments-body">
 <header className="appointments-header"><img className="appointments-logo" src="/assets/appointments/logo.png" alt="ترنم · اکوسیستم سلامت"/>
 <a className="appointments-bell" href={base+'/notifications'} aria-label="اعلان‌ها"><img src="/assets/appointments/bell.svg" width="22" height="22" alt=""/></a><h1>{title}</h1><p>{subtitle}</p></header>
 <main className="appointments-main booking-main">{children}</main>
 <nav className="appointments-nav" aria-label="ناوبری اصلی">{([['home','خانه',''],['record','پرونده','/records'],['calendar','نوبت‌ها','/appointments'],['account','حساب من','/account']] as const).map(([icon,label,path])=><a href={base+path} key={icon} aria-current={(activeSection==='account'?icon==='account':icon==='calendar')?'page':undefined}><img src={'/assets/appointments/'+(activeSection==='account'&&icon==='account'?'account-active':activeSection==='account'&&icon==='calendar'?'calendar-inactive':icon)+'.svg'} width="24" height="24" alt=""/>{label}</a>)}</nav>
 </div></div></div>;
}
type Load={kind:'loading'}|{kind:'error';message:string}|{kind:'ready';data:MemberSchedule};
export function FigmaBooking(){
 const centerRef=new URLSearchParams(window.location.search).get('center')?.trim()??'';
 const [scopeAttempt,setScopeAttempt]=useState(0);
 const [scopes,setScopes]=useState<string[]|null>(null),[memberRef,setMemberRef]=useState('');
 const [load,setLoad]=useState<Load>({kind:'loading'}),[refresh,setRefresh]=useState(0);
 const [selected,setSelected]=useState<OpenSlot|null>(null),[day,setDay]=useState(''),[review,setReview]=useState(false);
 const [status,setStatus]=useState<'idle'|'saving'|'uncertain'|'confirmed'>('idle');
 const [error,setError]=useState(''),[bookingRef,setBookingRef]=useState('');
 const alive=useRef(true),busy=useRef(false);
 const submitted=useRef<{memberRef:string;centerRef:string;slot:OpenSlot}|null>(null);
 useEffect(()=>{alive.current=true;return()=>{alive.current=false}},[]);
 useEffect(()=>{
  document.title='ترنم | '+(review?'بازبینی نوبت':'انتخاب زمان نوبت');
 },[review]);
 useEffect(()=>{
  if(!centerRef)return;
  const controller=new AbortController();
  getMemberScopes(controller.signal).then(items=>{if(!controller.signal.aborted){setScopes(items);setMemberRef(items.length===1?items[0]:'')}}).catch((e:unknown)=>{if(!controller.signal.aborted)setLoad({kind:'error',message:e instanceof Error?e.message:'دریافت دسترسی ناموفق بود.'})});
  return()=>controller.abort();
 },[centerRef,scopeAttempt]);
 useEffect(()=>{
  if(!centerRef||!memberRef)return;
  const controller=new AbortController();setLoad({kind:'loading'});setSelected(null);setReview(false);setDay('');
  getMemberSchedule(memberRef,centerRef,controller.signal).then(data=>{
   if(!controller.signal.aborted){const future=data.slots.filter(s=>Date.parse(s.startsAt)>Date.now()).sort((a,b)=>Date.parse(a.startsAt)-Date.parse(b.startsAt));setLoad({kind:'ready',data:{...data,slots:future}});setDay(future[0]?dayKey(future[0].startsAt):'')}
  }).catch((e:unknown)=>{if(!controller.signal.aborted)setLoad({kind:'error',message:e instanceof Error?e.message:'دریافت ظرفیت ناموفق بود.'})});
  return()=>controller.abort();
 },[centerRef,memberRef,refresh]);
 async function submit(){
  if(busy.current||status!=='idle'||load.kind!=='ready'||!selected||!memberRef||!review)return;
  if(Date.parse(selected.startsAt)<=Date.now()){setError('زمان انتخاب‌شده گذشته است؛ زمان دیگری انتخاب کنید.');setRefresh(x=>x+1);return}
  busy.current=true;setStatus('saving');setError('');submitted.current={memberRef,centerRef,slot:selected};
  try{
   const result=await bookMemberAppointment(memberRef,centerRef,{requestId:crypto.randomUUID(),expectedRevision:load.data.revision,slotRef:selected.slotRef});
   if(alive.current){setBookingRef(result.bookingRef);setStatus('confirmed')}
  }catch(e){
   if(!alive.current)return;
   if(e instanceof AppointmentApiError&&e.status>=400&&e.status<500){setStatus('idle');setError(e.message);setRefresh(x=>x+1)}
   else{setStatus('uncertain');setError('پاسخ قطعی ثبت دریافت نشد. برای جلوگیری از ثبت دوباره، نتیجه را از فهرست نوبت‌ها بررسی کنید.')}
  }finally{busy.current=false}
 }
 async function reconcile(){
  if(busy.current||!submitted.current)return;
  busy.current=true;setStatus('saving');
  try{
   const result=await getMyAppointments();const command=submitted.current;
   const found=result.appointments.find(item=>item.centerRef===command.centerRef&&item.memberRef===command.memberRef&&item.slotRef===command.slot.slotRef&&Date.parse(item.startsAt)===Date.parse(command.slot.startsAt)&&item.state==='Booked');
   if(alive.current){if(found){setBookingRef(found.bookingRef);setError('');setStatus('confirmed')}else{setStatus('uncertain');setError('هنوز ثبت نوبت تأیید نشده است؛ دوباره نتیجه را بررسی کنید.')}}
  }catch{if(alive.current){setStatus('uncertain');setError('دریافت نتیجه ناموفق بود؛ دوباره بررسی کنید.')}}finally{busy.current=false}
 }
 const days=load.kind==='ready'?[...new Set(load.data.slots.map(s=>dayKey(s.startsAt)))]:[];
 const locked=status!=='idle';
 return <AppointmentPage title={status==='confirmed'?'نوبت ثبت شد':review?'بازبینی نوبت':'انتخاب زمان نوبت'} subtitle={review?'پیش از ثبت، اطلاعات را بررسی کنید':'زمان مناسب خود را انتخاب کنید'}>
 {!centerRef?<section className="appointment-state"><h2>ابتدا مرکز مراجعه را انتخاب کنید</h2><p>از جزئیات یکی از نوبت‌های خود، «نوبت مجدد در همین مرکز» را انتخاب کنید. جست‌وجوی مراکز هنوز متصل نشده است.</p></section>:<>
 {error&&<p role="alert" className="appointment-error">{error}</p>}
 {status==='confirmed'?<section className="appointment-success" role="status"><h2>ثبت نوبت تأیید شد</h2><p>{selected&&date(selected.startsAt)}</p><a className="appointment-primary" href={base+'/appointment-detail?'+new URLSearchParams({booking:bookingRef,center:centerRef})}>مشاهدهٔ جزئیات نوبت</a></section>:<>
 {scopes?.length===0&&<p className="appointment-state" role="status">دسترسی نوبت‌گیری برای این حساب فعال نیست.</p>}
 {scopes&&scopes.length>1&&<label className="booking-account">حساب بیمار<select disabled={locked} value={memberRef} onChange={e=>{setMemberRef(e.target.value);setSelected(null)}}><option value="">انتخاب حساب مجاز</option>{scopes.map((scope,i)=><option key={scope} value={scope}>حساب مجاز {(i+1).toLocaleString('fa-IR')} · {scope}</option>)}</select></label>}
 {(!scopes||memberRef)&&load.kind==='loading'&&<p role="status">در حال دریافت ظرفیت مرکز…</p>}
 {load.kind==='error'&&<div className="appointment-state appointment-error"><p role="alert">{load.message}</p>{!memberRef&&<button className="appointment-refresh" onClick={()=>{setLoad({kind:'loading'});setScopeAttempt(x=>x+1)}}>تلاش دوباره برای دریافت دسترسی</button>}</div>}
 {load.kind==='ready'&&memberRef&&<>
 <section className="booking-provider"><h2>مرکز انتخاب‌شده</h2><p><bdi>{centerRef}</bdi></p>{selected&&<p>مرجع پزشک: <bdi>{selected.doctorRef}</bdi></p>}</section>
 {review&&selected?<>
 <section className="booking-review-time"><h2>زمان انتخاب‌شده</h2><time dateTime={selected.startsAt}>{date(selected.startsAt)}</time></section>
 <section className="booking-patient"><small>بیمار</small><p>حساب مجاز انتخاب‌شده</p><bdi>{memberRef}</bdi></section>
 <section className="booking-note"><h2>اطلاعات نوبت برای بیمار انتخاب‌شده ثبت می‌شود</h2><p>مرکز، پزشک و زمان را پیش از ثبت دوباره بررسی کنید.</p></section>
 <p className="booking-availability">ظرفیت نوبت پیش از نهایی‌شدن دوباره بررسی می‌شود.</p>
 {status==='uncertain'?<button className="appointment-primary booking-continue" onClick={()=>void reconcile()}>بررسی نتیجه ثبت</button>:<button className="appointment-primary booking-continue" disabled={locked} onClick={()=>void submit()}>{status==='saving'?'در حال ثبت…':'تأیید و ثبت نوبت'}</button>}
 <button className="appointment-refresh" disabled={locked} onClick={()=>setReview(false)}>تغییر زمان</button>
 </>:<>
 <h2 className="booking-label">انتخاب روز</h2><div className="booking-days" role="group" aria-label="روز مراجعه">{days.map(key=>{const slots=load.data.slots.filter(s=>dayKey(s.startsAt)===key);return <button key={key} aria-pressed={day===key} onClick={()=>{setDay(key);setSelected(null)}}><strong>{dayLabel(slots[0].startsAt)}</strong><small>{slots.length.toLocaleString('fa-IR')} زمان</small></button>})}</div>
 <h2 className="booking-label">زمان‌های قابل انتخاب</h2><div className="booking-times" role="group" aria-label="زمان مراجعه">{load.data.slots.filter(s=>dayKey(s.startsAt)===day).map(slot=><button key={slot.slotRef} aria-pressed={selected?.slotRef===slot.slotRef} onClick={()=>setSelected(slot)}>{time(slot.startsAt)}<small>پزشک: <bdi>{slot.doctorRef}</bdi></small></button>)}</div>
 {!load.data.slots.length&&<p role="status" className="appointment-state">ظرفیت آزادی در این مرکز وجود ندارد.</p>}
 <section className="booking-note"><h2>زمان‌ها از برنامهٔ مرکز دریافت شده‌اند</h2><p>انتخاب زمان تا تأیید نهایی، ظرفیت را برای شما نگه نمی‌دارد.</p></section>
 <button className="appointment-primary booking-continue" disabled={!selected} onClick={()=>{setReview(true);setError('')}}>ادامه پس از انتخاب زمان</button>
 </>}
 </>}
 </>}
 {status==='idle'&&memberRef&&<button className="appointment-refresh" onClick={()=>{setError('');setRefresh(x=>x+1)}}>تازه‌سازی ظرفیت</button>}
 </>}
 <a className="appointment-refresh" href={base+'/appointments'}>بازگشت به نوبت‌های من</a>
 </AppointmentPage>;
}
