import {useState} from 'react';

// TRN-TECH-102 — read-only, manually requested same-center department roster.
// Synthetic assignment and affiliation are NOT provider qualifications.
type Doctor={
  doctorRef:string;affiliation:'PendingIndependentVerification';
  assignmentUnverified:true;clinicalPrivilegesGranted:false;licensureVerified:false
};
type Roster={
  contract:'TRN-TECH-102';syntheticOnly:true;
  outcome:'UNVERIFIED_DEPARTMENT_ROSTER';centerRef:string;departmentRef:string;
  currentVersion:number;asOfVersion:number;doctorCount:number;doctors:Doctor[];
  historyReplayed:true;historicalOnly:boolean;
  managerAuthenticated:false;professionalQualificationVerified:false;
  clinicalAccessGranted:false;providerContractVerified:false;
  billingAllowed:false;nationalReleaseAllowed:false
};
const validRef=(v:string)=>/^test:[A-Za-z0-9_-]{1,123}$/.test(v)&&v.length<=128;
function valid(value:unknown,center:string,department:string,current:number,at:number):value is Roster{
  if(!value||typeof value!=='object')return false;
  const r=value as Partial<Roster>;
  if(r.contract!=='TRN-TECH-102'||r.syntheticOnly!==true||
     r.outcome!=='UNVERIFIED_DEPARTMENT_ROSTER'||r.centerRef!==center||
     r.departmentRef!==department||r.currentVersion!==current||r.asOfVersion!==at||
     r.historyReplayed!==true||r.historicalOnly!==(at<current)||
     r.managerAuthenticated!==false||r.professionalQualificationVerified!==false||
     r.clinicalAccessGranted!==false||r.providerContractVerified!==false||
     r.billingAllowed!==false||r.nationalReleaseAllowed!==false||
     !Array.isArray(r.doctors)||!Number.isSafeInteger(r.doctorCount)||
     r.doctorCount!==r.doctors.length)return false;
  let previous='';
  for(const d of r.doctors){
    if(!d||!validRef(d.doctorRef)||d.doctorRef<=previous||
       d.affiliation!=='PendingIndependentVerification'||d.assignmentUnverified!==true||
       d.clinicalPrivilegesGranted!==false||d.licensureVerified!==false)
      return false;
    previous=d.doctorRef;
  }
  return true;
}
export function DepartmentRoster({center,department,version}:{
  center:string;department:string;version:number
}){
  const [asOf,setAsOf]=useState(String(version));
  const [roster,setRoster]=useState<Roster|null>(null);
  const [pending,setPending]=useState(false);
  const [message,setMessage]=useState('فهرست این بخش هنوز مستقلاً بازخوانی نشده است.');
  async function read(){
    if(pending||!validRef(center)||!validRef(department)||!/^(0|[1-9][0-9]*)$/.test(asOf))
      return;
    const at=Number(asOf);
    if(!Number.isSafeInteger(at)||at>version)return;
    setPending(true);setRoster(null);
    try{
      const url='/internal/demo/v1/centers/'+encodeURIComponent(center)+
        '/departments/'+encodeURIComponent(department)+
        '/roster?expectedCurrentVersion='+version+'&asOfVersion='+at;
      const response=await fetch(url,{credentials:'omit',cache:'no-store'});
      const raw:unknown=await response.json();
      if(!response.ok||!valid(raw,center,department,version,at))
        throw Error('ROSTER_UNVERIFIED');
      setRoster(raw);
      setMessage('فهرست از رویدادهای ممیزی‌شده PostgreSQL بازسازی شد؛ پزشک تأیید نشده است.');
    }catch{
      setRoster(null);
      setMessage('بخش در این نسخه موجود نیست یا نسخه مرکز تغییر کرده؛ هیچ فهرستی معتبر فرض نمی‌شود.');
    }finally{setPending(false);}
  }
  return <section className="panel" aria-label="فهرست پزشکان بخش با نسخه ثابت"
    data-testid="department-roster">
    <h2>فهرست وابستگی پزشکان یک بخش — صرفاً آزمایشی</h2>
    <p>مرکز: <code>{center}</code> | بخش: <code>{department}</code> |
      نسخه فعلی پایگاه: {version}</p>
    <p>ارتباط پزشک با بخش در این محیط تنها یک ادعای آزمایشی و در انتظار تأیید مستقل است.
      پایان وابستگی، نام پزشک را از فهرست جاری حذف می‌کند ولی تاریخچه را پاک نمی‌کند.</p>
    <label>نسخه زمانی فهرست (صفر تا {version})
      <input aria-label="نسخه فهرست بخش" type="number" step="1" min="0"
        max={version} value={asOf}
        onChange={e=>{setAsOf(e.target.value);setRoster(null);}}/>
    </label>
    <button type="button" disabled={pending||!validRef(department)||
      !/^(0|[1-9][0-9]*)$/.test(asOf)||!Number.isSafeInteger(Number(asOf))||
      Number(asOf)>version} onClick={()=>void read()}>
      بازخوانی دستی فهرست بخش از PostgreSQL
    </button>
    {roster?<div data-testid="department-roster-results">
      <p>نسخه بازسازی‌شده: {roster.asOfVersion} |
        شمار وابستگی‌های تأییدنشده: {roster.doctorCount}</p>
      <ul>{roster.doctors.map(d=><li key={d.doctorRef}>
        <code>{d.doctorRef}</code> — در انتظار بررسی مستقل؛ بدون مجوز بالینی
      </li>)}</ul>
      {roster.doctors.length===0?<p>در این نسخه پزشک وابسته‌ای برای بخش ثبت نشده است.</p>:null}
      <p>تأیید مدیر: خیر | پروانه پزشک: تأییدنشده | دسترسی بیمار: ممنوع |
        دریافت وجه: غیرمجاز</p>
    </div>:null}
    <p role="status" data-testid="department-roster-status">{message}</p>
  </section>;
}
