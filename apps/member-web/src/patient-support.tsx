import {useEffect,useRef,useState,type FormEvent} from 'react';
import {AppointmentPage} from './figma-booking';
import {AppointmentApiError,createMySupportRequest,getMySupportRequests,type SupportCommand,type SupportPage,type SupportRequest,type SupportTopic} from './taranom-api';
import './patient-support.css';
const topics:Record<SupportTopic,string>={account:'حساب و عضویت',group:'اعضای گروه',appointments:'نوبت و خدمات',privacy:'حریم خصوصی'};
const formatDate=(value:string)=>new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Tehran'}).format(new Date(value));
export function PatientSupport(){
 const [topic,setTopic]=useState<SupportTopic|''>(''),[message,setMessage]=useState('');
 const [state,setState]=useState<'idle'|'saving'|'uncertain'|'confirmed'>('idle');
 const [error,setError]=useState(''),[confirmed,setConfirmed]=useState<SupportRequest|null>(null);
 const [historyOpen,setHistoryOpen]=useState(false),[history,setHistory]=useState<SupportPage|null>(null),[historyError,setHistoryError]=useState(''),[loading,setLoading]=useState(false),[cursor,setCursor]=useState<string|undefined>(),[refresh,setRefresh]=useState(0);
 const pending=useRef<SupportCommand|null>(null),busy=useRef(false),alive=useRef(true);
 useEffect(()=>{alive.current=true;document.title='ترنم | پشتیبانی';return()=>{alive.current=false}},[]);
 useEffect(()=>{
  if(!historyOpen)return;
  const controller=new AbortController();setHistory(null);setLoading(true);setHistoryError('');
  getMySupportRequests(cursor?{before:cursor}:{},controller.signal).then(result=>{if(!controller.signal.aborted)setHistory(result)}).catch((e:unknown)=>{if(!controller.signal.aborted)setHistoryError(e instanceof Error?e.message:'دریافت درخواست‌ها ناموفق بود.')}).finally(()=>{if(!controller.signal.aborted)setLoading(false)});
  return()=>controller.abort();
 },[historyOpen,cursor,refresh]);
 function applySuccess(request:SupportRequest){
  if(!alive.current)return;
  setConfirmed(request);setState('confirmed');setError('');setMessage('');setTopic('');pending.current=null;setCursor(undefined);setRefresh(x=>x+1);
 }
 async function submit(e:FormEvent){
  e.preventDefault();if(busy.current||state!=='idle'||!topic||message.trim().length<10)return;
  busy.current=true;setState('saving');setError('');const command={requestId:crypto.randomUUID(),topic,message:message.trim()};pending.current=command;
  try{applySuccess(await createMySupportRequest(command))}
  catch(e){if(alive.current){setError(e instanceof Error?e.message:'پاسخ ثبت درخواست دریافت نشد.');if(e instanceof AppointmentApiError&&e.status>=400&&e.status<500){setState('idle');pending.current=null}else setState('uncertain')}}
  finally{busy.current=false}
 }
 async function reconcile(){
  if(busy.current||!pending.current)return;
  const command=pending.current;busy.current=true;setState('saving');
  try{
   const result=await getMySupportRequests({requestId:command.requestId});
   const found=result.requests.find(item=>item.requestId===command.requestId&&item.topic===command.topic&&item.message===command.message);
   if(found)applySuccess(found);else if(alive.current){setState('uncertain');setError('ثبت درخواست هنوز تأیید نشده است؛ نتیجه را دوباره بررسی کنید.')}
  }catch{if(alive.current){setState('uncertain');setError('دریافت نتیجه ناموفق بود. دوباره بررسی کنید.')}}finally{busy.current=false}
 }
 const locked=state!=='idle';
 return <AppointmentPage title="پشتیبانی ترنم" subtitle="راهنمای حساب و خدمات ترنم" activeSection="account"><section className="patient-support">
 <h2>درخواست خود را ثبت کنید</h2><p className="support-intro">موضوع را انتخاب کنید و توضیح کوتاهی بنویسید.</p>
 {error&&<p className="support-error" role="alert">{error}</p>}
 {confirmed&&<section className="support-receipt" role="status"><h3>درخواست پشتیبانی ثبت شد</h3><p>کد پیگیری: <bdi>{confirmed.requestRef}</bdi></p><p>{formatDate(confirmed.createdAt)} · ثبت‌شده</p></section>}
 <form onSubmit={e=>void submit(e)}>
  <label className="support-topic">موضوع درخواست<select required value={topic} disabled={locked} onChange={e=>setTopic(e.target.value as SupportTopic|'')}><option value="">انتخاب موضوع</option>{Object.entries(topics).map(([key,label])=><option key={key} value={key}>{label}</option>)}</select></label>
  <label className="support-description"><span id="support-message-label">شرح درخواست</span><textarea aria-labelledby="support-message-label" aria-describedby="support-message-note" required minLength={10} maxLength={4000} value={message} disabled={locked} onChange={e=>setMessage(e.target.value)} placeholder="مشکل یا پرسش خود را توضیح دهید…"/><small id="support-message-note">اطلاعات پزشکی غیرضروری را وارد نکنید.</small></label>
  <p className="support-emergency">پشتیبانی برای مسائل حساب و سامانه است؛ در وضعیت اورژانسی با اورژانس تماس بگیرید.</p>
  {state==='idle'&&<button type="submit" className="support-primary">ثبت درخواست پشتیبانی</button>}
  {state==='saving'&&<button type="button" disabled className="support-primary">در حال بررسی…</button>}
  {state==='uncertain'&&<button type="button" className="support-primary" onClick={()=>void reconcile()}>بررسی نتیجه ثبت</button>}
  {state==='confirmed'&&<button type="button" className="support-primary" onClick={()=>{setState('idle');setConfirmed(null)}}>ثبت درخواست دیگر</button>}
 </form>
 <p className="support-topics">حساب و عضویت · اعضای گروه · نوبت و خدمات · حریم خصوصی</p>
 <button type="button" className="support-history-toggle" aria-expanded={historyOpen} onClick={()=>setHistoryOpen(x=>!x)}>{historyOpen?'بستن درخواست‌های من':'درخواست‌های من'}</button>
 {historyOpen&&<section className="support-history" aria-labelledby="support-history-title"><h2 id="support-history-title">درخواست‌های من</h2>
  {loading&&<p role="status">در حال دریافت درخواست‌ها…</p>}{historyError&&<p className="support-error" role="alert">{historyError}</p>}
  {history?.requests.map(item=><article key={item.requestRef}><strong>{topics[item.topic]}</strong><span>{item.state==='Answered'?'پاسخ داده‌شده':'ثبت‌شده'}</span><p className="support-tracking"><bdi>{item.requestRef}</bdi></p><time dateTime={item.createdAt}>{formatDate(item.createdAt)}</time><p className="support-message">{item.message}</p>{item.reply&&item.repliedAt&&<section className="support-reply"><h3>پاسخ پشتیبانی</h3><time dateTime={item.repliedAt}>{formatDate(item.repliedAt)}</time><p className="support-message">{item.reply}</p></section>}</article>)}
  {history&&!history.requests.length&&<p role="status">درخواستی برای نمایش وجود ندارد.</p>}
  <div className="support-history-actions"><button disabled={loading} onClick={()=>{setCursor(undefined);setRefresh(x=>x+1)}}>تازه‌سازی درخواست‌ها</button>{history?.nextBefore&&<button disabled={loading} onClick={()=>setCursor(history.nextBefore!)}>درخواست‌های قدیمی‌تر</button>}</div>
 </section>}
 </section></AppointmentPage>;
}
