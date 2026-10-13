import {useState} from 'react';

// TRN-TECH-104: one canonical PostgreSQL source read for BOTH audit page
// and reconstructed as-of state. Test only, with all authority denied.
type Doctor={doctorRef:string;affiliation:'PendingIndependentVerification'|'Ended';departmentRefs:string[]};
type Snapshot={
  contract:'TRN-TECH-073';syntheticOnly:true;
  managerAuthenticated:false;professionalQualificationVerified:false;
  nationalReleaseAllowed:false;centerRef:string;currentVersion:number;
  eventCount:number;outcome:string;departments:string[];doctors:Doctor[]
};
type Event={
  version:number;kind:'DefineDepartment'|'RecordDoctorAffiliation'|
  'AssignDoctorToDepartment'|'EndDoctorAffiliation';
  departmentRef:string|null;doctorRef:string|null;
  hypotheticalOnly:true;professionalQualificationVerified:false;
  clinicalPrivilegesGranted:false
};
type Workspace={
  contract:'TRN-TECH-104';syntheticOnly:true;
  outcome:'VERSION_PINNED_CENTER_AUDIT_WORKSPACE';
  centerRef:string;currentVersion:number;asOfVersion:number;afterVersion:number;
  snapshot:Snapshot;events:Event[];hasMore:boolean;nextAfterVersion:number|null;
  sourceReplayed:true;sameSourceSnapshot:true;durableCrossRequestSnapshot:false;
  managerAuthenticated:false;professionalQualificationVerified:false;
  clinicalAccessGranted:false;providerContractVerified:false;
  billingAllowed:false;nationalReleaseAllowed:false
};
const testRef=(s:string)=>/^test:[A-Za-z0-9_-]{1,123}$/.test(s)&&s.length<=128;
function valid(raw:unknown,center:string,current:number,asOf:number,after:number):raw is Workspace{
  if(!raw||typeof raw!=='object')return false;
  const a=raw as Partial<Workspace>;
  if(a.contract!=='TRN-TECH-104'||a.syntheticOnly!==true||
     a.outcome!=='VERSION_PINNED_CENTER_AUDIT_WORKSPACE'||
     a.centerRef!==center||a.currentVersion!==current||a.asOfVersion!==asOf||
     a.afterVersion!==after||a.sourceReplayed!==true||
     a.sameSourceSnapshot!==true||a.durableCrossRequestSnapshot!==false||
     a.managerAuthenticated!==false||a.professionalQualificationVerified!==false||
     a.clinicalAccessGranted!==false||a.providerContractVerified!==false||
     a.billingAllowed!==false||a.nationalReleaseAllowed!==false||
     !a.snapshot||typeof a.snapshot!=='object')return false;
  const s=a.snapshot;
  if(s.contract!=='TRN-TECH-073'||s.syntheticOnly!==true||
     s.managerAuthenticated!==false||s.professionalQualificationVerified!==false||
     s.nationalReleaseAllowed!==false||s.centerRef!==center||
     s.currentVersion!==asOf||s.eventCount!==asOf||
     !Array.isArray(s.departments)||!s.departments.every(d=>typeof d==='string'&&testRef(d))||
     !Array.isArray(s.doctors)||!s.doctors.every(d=>d&&testRef(d.doctorRef)&&
       ['PendingIndependentVerification','Ended'].includes(d.affiliation)&&
       Array.isArray(d.departmentRefs)&&
       d.departmentRefs.every(v=>testRef(v))&&
       (d.affiliation!=='Ended'||d.departmentRefs.length===0)))return false;
  if(!Array.isArray(a.events)||a.events.length>3||typeof a.hasMore!=='boolean')return false;
  for(let i=0;i<a.events.length;i++){
    const e=a.events[i];
    if(!e||e.version!==after+i+1||e.version>asOf||
       !['DefineDepartment','RecordDoctorAffiliation','AssignDoctorToDepartment','EndDoctorAffiliation']
         .includes(e.kind)||
       (e.departmentRef!==null&&!testRef(e.departmentRef))||
       (e.doctorRef!==null&&!testRef(e.doctorRef))||
       e.hypotheticalOnly!==true||e.professionalQualificationVerified!==false||
       e.clinicalPrivilegesGranted!==false)return false;
  }
  return a.hasMore
    ? a.events.length===3&&a.nextAfterVersion===after+3
    : a.nextAfterVersion===null;
}
export function CenterAuditWorkspace({center,version}:{
  center:string;version:number;
}){
  const [asOf,setAsOf]=useState(String(version));
  const [observed,setObserved]=useState<Workspace|null>(null);
  const [events,setEvents]=useState<Event[]>([]);
  const [cursor,setCursor]=useState(0);
  const [more,setMore]=useState(false);
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('ممیزی یکپارچه هنوز درخواست نشده است.');
  const number=Number(asOf);
  const allowed=testRef(center)&&/^(0|[1-9][0-9]*)$/.test(asOf)&&
    Number.isSafeInteger(number)&&number>=0&&number<=version;
  async function read(after:number){
    if(busy||!allowed)return;
    setBusy(true);
    if(after===0){setObserved(null);setEvents([]);setCursor(0);setMore(false);}
    try{
      const url='/internal/demo/v1/centers/'+encodeURIComponent(center)+
        '/audit-workspace?expectedCurrentVersion='+version+
        '&asOfVersion='+number+'&afterVersion='+after+'&limit=3';
      const reply=await fetch(url,{credentials:'omit',cache:'no-store'});
      const raw:unknown=await reply.json();
      if(!reply.ok||!valid(raw,center,version,number,after))
        throw Error('AUDIT_NOT_VERIFIABLE');
      setObserved(raw);
      setEvents(old=>after===0?raw.events:[...old,...raw.events]);
      setCursor(raw.hasMore?raw.nextAfterVersion!:after+raw.events.length);
      setMore(raw.hasMore);
      setMessage('رخدادها و وضعیت نسخه تاریخی از یک منبع ممیزی‌شده بازسازی شدند.');
    }catch{
      // A stale or unverifiable page invalidates the entire combined view.
      setObserved(null);setEvents([]);setCursor(0);setMore(false);
      setMessage('نسخه مرکز تغییر کرده یا پاسخ قابل تأیید نیست؛ کل نمای ممیزی پاک شد.');
    }finally{setBusy(false);}
  }
  return <section className="panel" data-testid="center-audit-workspace"
    aria-label="ممیزی نسخه‌ثابت مرکز">
    <h2>ممیزی یکپارچه و نسخه‌دار مرکز</h2>
    <p>رخدادها و وضعیت بخش‌ها و پزشکان از همان نسخه PostgreSQL بازیابی می‌شوند.
      این محیط صرفاً آزمایشی است؛ هیچ صلاحیت پزشکی یا اختیار مدیریت تأیید نشده است.</p>
    <p>نسخه جاری قابل مشاهده: {version}</p>
    <label>نسخه مورد بررسی
      <input aria-label="نسخه ممیزی مرکز" type="number" step="1" min="0"
        max={version} value={asOf}
        onChange={e=>{setAsOf(e.target.value);setObserved(null);setEvents([]);setMore(false);setCursor(0);}}/>
    </label>
    <button type="button" disabled={!allowed||busy}
      onClick={()=>void read(0)}>بازخوانی یکپارچه ممیزی</button>
    {more?<button type="button" disabled={!allowed||busy}
      onClick={()=>void read(cursor)}>صفحه بعد همان نسخه ممیزی</button>:null}
    {observed?<div data-testid="center-audit-result">
      <p>نسخه بازسازی‌شده: {observed.asOfVersion} · تعداد کل رویدادهای تا آن نسخه: {observed.snapshot.eventCount}</p>
      <p>بخش‌ها: {observed.snapshot.departments.length?
        observed.snapshot.departments.join('، '):'هیچ'}</p>
      <ul>{observed.snapshot.doctors.map(d=><li key={d.doctorRef}>
        <code>{d.doctorRef}</code> — {d.affiliation==='Ended'?'همکاری پایان یافته':'در انتظار تأیید مستقل'}
        ؛ بخش‌های مرتبط: {d.departmentRefs.length?d.departmentRefs.join('، '):'هیچ'}
      </li>)}</ul>
      <ol data-testid="center-audit-events">{events.map(e=><li key={e.version}>
        نسخه {e.version} — {e.kind} (ادعای آزمایشی)
      </li>)}</ol>
      <p>تأیید مدیر: خیر | صلاحیت حرفه‌ای: تأییدنشده |
        دسترسی درمانی: غیرمجاز | صدور صورتحساب: غیرمجاز</p>
    </div>:null}
    <p role="status" data-testid="center-audit-status">{message}</p>
  </section>;
}
