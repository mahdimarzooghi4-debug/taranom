import {useState} from 'react';

// TRN-TECH-098 — manual read-only historical review of canonical PG events.
type Source={previewId:string;previewRevision:number;previewFingerprint:string;choiceRevision:number};
type Event={revision:number;choice:string;hypotheticalOnly:boolean;legallyBinding:boolean};
type Shared={contract:string;syntheticOnly:boolean;snapshotIdentity:string;
  previewRevision:number;previewFingerprint:string;currentRevision:number;
  expectedCurrentRevision:number;memberAuthenticated:boolean;
  officialChoiceRecorded:boolean;choiceSignatureValid:boolean;
  insurerEntitlementVerified:boolean;walletMutated:boolean;
  financialCommitmentCreated:boolean;clinicalConsentGranted:boolean;
  nationalReleaseAllowed:boolean};
type Page=Shared&{outcome:string;afterRevision:number;hasMore:boolean;
  nextAfterRevision:number|null;events:Event[]};
type Historical=Shared&{outcome:string;asOfRevision:number;choiceCount:number;
  lastUnverifiedChoice:string|null;historicalOnly:boolean;autoCarriedForward:boolean};
function shared(raw:unknown,s:Source):raw is Shared {
  if(!raw||typeof raw!=='object')return false;
  const v=raw as Partial<Shared>;
  return v.contract==='TRN-TECH-098'&&v.syntheticOnly===true&&
    v.snapshotIdentity===s.previewId&&v.previewRevision===s.previewRevision&&
    v.previewFingerprint===s.previewFingerprint&&
    v.currentRevision===s.choiceRevision&&
    v.expectedCurrentRevision===s.choiceRevision&&
    v.memberAuthenticated===false&&v.officialChoiceRecorded===false&&
    v.choiceSignatureValid===false&&v.insurerEntitlementVerified===false&&
    v.walletMutated===false&&v.financialCommitmentCreated===false&&
    v.clinicalConsentGranted===false&&v.nationalReleaseAllowed===false;
}
function pageValid(raw:unknown,s:Source,after:number):raw is Page {
  if(!shared(raw,s))return false;
  const v=raw as Page;
  if(v.outcome!=='VERIFIED_SYNTHETIC_HISTORY_PAGE'||
     v.afterRevision!==after||!Array.isArray(v.events)||
     v.events.length>5||typeof v.hasMore!=='boolean')return false;
  for(let i=0;i<v.events.length;i++){
    const e=v.events[i];
    if(e.revision!==after+i+1||
       !['AcknowledgedUncertainty','Deferred','RequestedClarification'].includes(e.choice)||
       e.hypotheticalOnly!==true||e.legallyBinding!==false)return false;
  }
  return v.hasMore
    ? v.events.length===5&&v.nextAfterRevision===after+5
    : v.nextAfterRevision===null;
}
function historicalValid(raw:unknown,s:Source,revision:number):raw is Historical {
  if(!shared(raw,s))return false;
  const v=raw as Historical;
  return v.outcome==='HISTORICAL_UNVERIFIED_CHOICE_READ'&&
    v.asOfRevision===revision&&v.choiceCount===revision&&
    v.historicalOnly===true&&v.autoCarriedForward===false&&
    (v.lastUnverifiedChoice===null||
      ['AcknowledgedUncertainty','Deferred','RequestedClarification']
        .includes(v.lastUnverifiedChoice));
}
export function CostHistoryPanel({source}:{source:Source}){
  const [events,setEvents]=useState<Event[]>([]);
  const [cursor,setCursor]=useState(0);
  const [more,setMore]=useState(false);
  const [historical,setHistorical]=useState<Historical|null>(null);
  const [at,setAt]=useState('0');
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('تاریخچه انتخاب‌ها هنوز بازخوانی نشده است.');
  const url='/internal/demo/v1/cost-previews/'+encodeURIComponent(source.previewId);
  async function readPage(after:number){
    if(busy)return;
    setBusy(true);setHistorical(null);
    try{
      const res=await fetch(url+'/history?afterRevision='+after+
        '&limit=5&expectedCurrentRevision='+source.choiceRevision,
        {credentials:'omit',cache:'no-store'});
      const raw:unknown=await res.json();
      if(!res.ok||!pageValid(raw,source,after))throw Error('INVALID_PAGE');
      setEvents(prev=>after===0?raw.events:[...prev,...raw.events]);
      setCursor(raw.hasMore?raw.nextAfterRevision!:after+raw.events.length);
      setMore(raw.hasMore);
      setMessage('صفحه تاریخچه از رویدادهای واقعی Journal آزمایشی تأیید شد.');
    }catch{
      setEvents([]);setCursor(0);setMore(false);
      setMessage('صفحه تاریخچه یا نسخه Journal تغییر کرده است؛ مرور دوباره لازم است.');
    }finally{setBusy(false);}
  }
  async function atRevision(){
    if(busy||!(/^(0|[1-9][0-9]*)$/.test(at)))return;
    const requested=Number(at);
    if(!Number.isSafeInteger(requested)||requested>source.choiceRevision)return;
    setBusy(true);setHistorical(null);
    try{
      const res=await fetch(url+'/versions/'+requested+
        '?expectedCurrentRevision='+source.choiceRevision,
        {credentials:'omit',cache:'no-store'});
      const raw:unknown=await res.json();
      if(!res.ok||!historicalValid(raw,source,requested))throw Error('INVALID_HISTORY');
      setHistorical(raw);
      setMessage('وضعیت نسخه گذشته مستقل و غیرالزام‌آور بازخوانی شد.');
    }catch{
      setMessage('نسخه تاریخی معتبر نیست یا تاریخچه در حین مرور تغییر کرده است.');
    }finally{setBusy(false);}
  }
  return <section data-testid="cost-history-panel" className="panel"
    aria-label="تاریخچه نسخه‌دار انتخاب هزینه">
    <h3>تاریخچه انتخاب‌های هزینه، فقط آزمایشی</h3>
    <p className="muted">TRN-TECH-098 · فقط خواندن رویدادهای PostgreSQL.
      شماره نسخه برای تمام صفحات ثابت می‌ماند؛ تغییر هم‌زمان موجب رد خواندن می‌شود.</p>
    <button type="button" disabled={busy} onClick={()=>void readPage(0)}>
      خواندن دستی صفحه اول تاریخچه
    </button>
    {more?<button type="button" disabled={busy} onClick={()=>void readPage(cursor)}>
      صفحه بعدِ همان نسخه
    </button>:null}
    <ol data-testid="cost-history-events">
      {events.map(e=><li key={e.revision}>
        نسخه {e.revision}: {e.choice} — صرفاً ادعای غیرقطعی
      </li>)}
    </ol>
    <label>نسخه تاریخی انتخاب (از صفر تا {source.choiceRevision})
      <input type="number" min="0" max={source.choiceRevision} step="1"
        aria-label="نسخه تاریخی هزینه" value={at}
        onChange={e=>{setAt(e.target.value);setHistorical(null);}}/>
    </label>
    <button type="button" disabled={busy||!(/^(0|[1-9][0-9]*)$/.test(at))||
      !Number.isSafeInteger(Number(at))||Number(at)>source.choiceRevision}
      onClick={()=>void atRevision()}>بازخوانی دستی وضعیت آن نسخه</button>
    {historical?<div data-testid="cost-history-asof">
      <p>نسخه تاریخی: {historical.asOfRevision} · شمار انتخاب: {historical.choiceCount}</p>
      <p>آخرین ادعای انتخاب در آن نسخه: {historical.lastUnverifiedChoice??'هیچ'}</p>
      <p>انتخاب قدیمی به نسخه جدید منتقل نشده است.</p>
    </div>:null}
    <p data-testid="cost-history-message" role="status">{message}</p>
  </section>;
}
