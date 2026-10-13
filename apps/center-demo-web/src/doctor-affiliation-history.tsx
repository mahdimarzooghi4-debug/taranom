import {useState} from 'react';

// TRN-TECH-103: exact-center doctor-specific, read-only PG event history.
// Historical claims never authenticate a center or verify a physician licence.
type Transition={
  version:number;kind:'RecordDoctorAffiliation'|'AssignDoctorToDepartment'|'EndDoctorAffiliation';
  departmentRef:string|null;hypotheticalOnly:true;licenseVerified:false;
  clinicalPrivilegesGranted:false;
};
type Result={
  contract:'TRN-TECH-103';syntheticOnly:true;
  outcome:'UNVERIFIED_DOCTOR_AFFILIATION_HISTORY';
  centerRef:string;doctorRef:string;currentVersion:number;asOfVersion:number;
  afterVersion:number;affiliation:'PendingIndependentVerification'|'Ended';
  departmentRefs:string[];historyReplayed:true;historicalOnly:boolean;
  events:Transition[];hasMore:boolean;nextAfterVersion:number|null;
  managerAuthenticated:false;professionalQualificationVerified:false;
  medicalLicenseVerified:false;clinicalAccessGranted:false;
  providerContractVerified:false;billingAllowed:false;nationalReleaseAllowed:false;
};
const validRef=(s:string)=>/^test:[A-Za-z0-9_-]{1,123}$/.test(s)&&s.length<=128;
const kinds=new Set(['RecordDoctorAffiliation','AssignDoctorToDepartment','EndDoctorAffiliation']);
function valid(raw:unknown,center:string,doctor:string,current:number,at:number,after:number):raw is Result{
  if(!raw||typeof raw!=='object')return false;
  const r=raw as Partial<Result>;
  if(r.contract!=='TRN-TECH-103'||r.syntheticOnly!==true||
    r.outcome!=='UNVERIFIED_DOCTOR_AFFILIATION_HISTORY'||
    r.centerRef!==center||r.doctorRef!==doctor||r.currentVersion!==current||
    r.asOfVersion!==at||r.afterVersion!==after||r.historyReplayed!==true||
    r.historicalOnly!==(at<current)||
    r.managerAuthenticated!==false||r.professionalQualificationVerified!==false||
    r.medicalLicenseVerified!==false||r.clinicalAccessGranted!==false||
    r.providerContractVerified!==false||r.billingAllowed!==false||
    r.nationalReleaseAllowed!==false||
    !['PendingIndependentVerification','Ended'].includes(String(r.affiliation))||
    !Array.isArray(r.departmentRefs)||r.departmentRefs.some(s=>!validRef(s))||
    !Array.isArray(r.events)||r.events.length>3||
    typeof r.hasMore!=='boolean')return false;
  let old='';
  for(const dep of r.departmentRefs){if(dep<=old)return false;old=dep;}
  if(r.affiliation==='Ended'&&r.departmentRefs.length>0)return false;
  let cursor=after;
  for(const event of r.events){
    if(!event||!Number.isSafeInteger(event.version)||event.version<=cursor||
      event.version>at||!kinds.has(event.kind)||
      (event.departmentRef!==null&&!validRef(event.departmentRef))||
      (event.kind==='AssignDoctorToDepartment')!==(event.departmentRef!==null)||
      event.hypotheticalOnly!==true||event.licenseVerified!==false||
      event.clinicalPrivilegesGranted!==false)return false;
    cursor=event.version;
  }
  return r.hasMore?r.events.length===3&&r.nextAfterVersion===cursor:
    r.nextAfterVersion===null;
}
export function DoctorAffiliationHistory({center,doctor,version}:{
  center:string;doctor:string;version:number;
}){
  const [asOf,setAsOf]=useState(String(version));
  const [events,setEvents]=useState<Transition[]>([]);
  const [result,setResult]=useState<Result|null>(null);
  const [cursor,setCursor]=useState(0);
  const [more,setMore]=useState(false);
  const [busy,setBusy]=useState(false);
  const [message,setMessage]=useState('تاریخچه این پزشک هنوز از PostgreSQL بازخوانی نشده است.');
  const selected=Number(asOf);
  const canRead=validRef(center)&&validRef(doctor)&&
    /^(0|[1-9][0-9]*)$/.test(asOf)&&Number.isSafeInteger(selected)&&selected<=version;
  async function read(after:number){
    if(busy||!canRead)return;
    setBusy(true);if(after===0){setEvents([]);setResult(null);setMore(false);}
    try{
      const url='/internal/demo/v1/centers/'+encodeURIComponent(center)+
        '/doctors/'+encodeURIComponent(doctor)+
        '/affiliation-history?expectedCurrentVersion='+version+
        '&asOfVersion='+selected+'&afterVersion='+after+'&limit=3';
      const response=await fetch(url,{credentials:'omit',cache:'no-store'});
      const raw:unknown=await response.json();
      if(!response.ok||!valid(raw,center,doctor,version,selected,after))
        throw Error('UNVERIFIED_DOCTOR_HISTORY');
      setEvents(old=>after===0?raw.events:[...old,...raw.events]);
      setResult(raw);
      setMore(raw.hasMore);
      setCursor(raw.hasMore?raw.nextAfterVersion!:after+raw.events.length);
      setMessage('تاریخچه از رویدادهای ممیزی‌شده مرکز بازیابی شد؛ صلاحیت پزشکی تأیید نشده است.');
    }catch{
      setEvents([]);setResult(null);setMore(false);setCursor(0);
      setMessage('نسخه مرکز تغییر کرده یا وابستگی ثبت نشده است؛ سابقه قبلی معتبر فرض نمی‌شود.');
    }finally{setBusy(false);}
  }
  return <section className="panel" aria-label="سابقه وابستگی پزشک در مرکز"
    data-testid="doctor-affiliation-history">
    <h2>سابقه وابستگی پزشک به بخش‌های همین مرکز</h2>
    <p>مرکز: <code>{center}</code> | پزشک آزمایشی: <code>{doctor}</code> |
      نسخه جاری مرکز: {version}</p>
    <p>هر رخداد صرفاً یک ادعای آزمایشی است؛ انتساب یا پایان همکاری
      جایگزین احراز هویت، بررسی پروانه یا مجوز فعالیت درمانی نیست.</p>
    <label>نسخه تاریخی وابستگی (۰ تا {version})
      <input aria-label="نسخه تاریخچه پزشک" type="number" step="1" min="0" max={version}
        value={asOf} onChange={e=>{setAsOf(e.target.value);
          setEvents([]);setResult(null);setMore(false);setCursor(0);}}/>
    </label>
    <button type="button" disabled={busy||!canRead}
      onClick={()=>void read(0)}>بازخوانی دستی سابقه پزشک</button>
    {more?<button type="button" disabled={busy||!canRead}
      onClick={()=>void read(cursor)}>صفحه بعد سابقه همان نسخه</button>:null}
    {result?<div data-testid="doctor-history-result">
      <p>وضعیت وابستگی: {result.affiliation==='Ended'?'پایان‌یافته':'در انتظار بررسی مستقل'}</p>
      <p>بخش‌های همان نسخه: {result.departmentRefs.length?
        result.departmentRefs.join('، '):'هیچ'}</p>
      <p>مدیر احراز شده: خیر | پروانه: تأییدنشده | دسترسی درمانی: خیر |
        قرارداد: تأییدنشده | پرداخت: غیرمجاز</p>
      <ol data-testid="doctor-history-events">{events.map(e=><li key={e.version}>
        نسخه {e.version}: {e.kind}
        {e.departmentRef?' — '+e.departmentRef:''} (غیررسمی)
      </li>)}</ol>
    </div>:null}
    <p role="status" data-testid="doctor-history-status">{message}</p>
  </section>;
}
