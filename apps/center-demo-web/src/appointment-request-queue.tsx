import {useState} from 'react';

type RequestRow={
  requestRef:string;centerRef:string;revision:number;
  departmentRef:string;requestedDoctorRef:string;slotRef:string
};
type Queue={
  contract:'TRN-TECH-217';syntheticOnly:true;
  source:'VERIFIED_ISOLATED_POSTGRES_APPOINTMENT_JOURNAL';
  centerRef:string;departmentRefFilter:string;requestedDoctorRefFilter:string;
  afterRequestRef:string;limit:10;
  hasMore:boolean;nextAfterRequestRef:string|null;requests:RequestRow[];
  memberAuthenticated:false;centerAuthenticated:false;
  requestedDoctorLicensed:false;slotAvailabilityVerified:false;
  bookingConfirmed:false;clinicalActionAllowed:false;
  nationalReleaseAllowed:false
};
const limit=10;
const isRef=(x:unknown):x is string=>
  typeof x==='string'&&x.length<=128&&/^test:[A-Za-z0-9_:-]{1,123}$/.test(x);
const falseKeys=[
 'memberAuthenticated','centerAuthenticated','requestedDoctorLicensed',
 'slotAvailabilityVerified','bookingConfirmed','clinicalActionAllowed',
 'nationalReleaseAllowed'
] as const;
function verified(value:unknown,center:string,departmentFilter:string,doctorFilter:string,after:string):value is Queue{
 if(!value||typeof value!=='object'||Array.isArray(value))return false;
 const q=value as Partial<Queue>;
 if(q.contract!=='TRN-TECH-217'||q.syntheticOnly!==true||
   q.source!=='VERIFIED_ISOLATED_POSTGRES_APPOINTMENT_JOURNAL'||
   q.centerRef!==center||q.departmentRefFilter!==departmentFilter||
   q.requestedDoctorRefFilter!==doctorFilter||q.afterRequestRef!==after||q.limit!==limit||
   !falseKeys.every(key=>q[key]===false)||
   typeof q.hasMore!=='boolean'||!Array.isArray(q.requests)||
   q.requests.length>limit)return false;
 let previous=after;
 for(const row of q.requests){
  if(!row||!isRef(row.requestRef)||row.requestRef<=previous||
    row.centerRef!==center||!Number.isSafeInteger(row.revision)||row.revision<1||
    !isRef(row.departmentRef)||!isRef(row.requestedDoctorRef)||!isRef(row.slotRef)||
    Object.keys(row).some(k=>!['requestRef','centerRef','revision',
      'departmentRef','requestedDoctorRef','slotRef'].includes(k)))return false;
  previous=row.requestRef;
 }
 return q.hasMore?q.requests.length===limit&&q.nextAfterRequestRef===previous:
   q.nextAfterRequestRef===null;
}

export function AppointmentRequestQueueQa(){
 const [center,setCenter]=useState('test:center-demo');
 const [departmentFilter,setDepartmentFilter]=useState('');
 const [doctorFilter,setDoctorFilter]=useState('');
 const [page,setPage]=useState<Queue|null>(null);
 const [status,setStatus]=useState('صف هنوز از ژورنال PostgreSQL خوانده نشده است.');
 const [pending,setPending]=useState(false);

 async function read(
  after='',
  selectedDepartment=departmentFilter.trim(),
  selectedDoctor=doctorFilter.trim(),
){
  if(pending)return;
  if(!isRef(center)){
   setPage(null);setStatus('فقط شناسه مرکز آزمایشی با پیشوند test: پذیرفته می‌شود.');
   return;
  }
  if((selectedDepartment&&!isRef(selectedDepartment))||
     (selectedDoctor&&!isRef(selectedDoctor))){
   setPage(null);setStatus('فیلترها باید شناسه ساختگی با پیشوند test: باشند.');
   return;
  }
  setPending(true);setPage(null);
  try{
   const query=new URLSearchParams({limit:String(limit)});
   if(after)query.set('afterRequestRef',after);
   if(selectedDepartment)query.set('departmentRef',selectedDepartment);
   if(selectedDoctor)query.set('requestedDoctorRef',selectedDoctor);
   const response=await fetch('/internal/demo/v1/centers/'+
     encodeURIComponent(center)+'/appointment-requests?'+query.toString(),
     {credentials:'omit',cache:'no-store'});
   if(!response.ok)throw Error('QUEUE_READ_FAILED');
   const raw:unknown=await response.json();
   if(!verified(raw,center,selectedDepartment,selectedDoctor,after))
    throw Error('QUEUE_UNVERIFIED');
   setPage(raw);
   setStatus('صف فقط‌خواندنی از PostgreSQL خوانده و یکپارچگی درخواست‌ها بررسی شد.');
  }catch{
   setStatus('صف یا یکپارچگی آن تأیید نشد؛ هیچ ردیفی نمایش داده نمی‌شود.');
  }finally{setPending(false);}
 }

 return <main id="main">
  <header>
   <p>ترنم | آزمون فنی مرکز درمانی</p>
   <h1>صف درخواست‌های نوبت آزمایشی</h1>
   <p>این صفحه فقط درخواست ثبت‌شده را از ژورنال PostgreSQL می‌خواند؛ هیچ پذیرش، رد، رزرو یا تأیید نوبتی ندارد.</p>
  </header>
  <section className="notice" role="note">
   <strong>فقط محیط آزمون — داده‌ها ساختگی‌اند</strong>
   <p>مرکز و پزشک احراز نشده‌اند؛ ظرفیت نوبت بررسی نشده و هیچ رزرو یا اقدام بالینی انجام نمی‌شود. انتخاب پزشکِ درخواست‌شده متعلق به بیمار باقی می‌ماند.</p>
  </section>
  <section className="panel" aria-labelledby="appointment-queue-heading">
   <h2 id="appointment-queue-heading">درخواست‌های ثبت‌شده برای مرکز آزمایشی</h2>
   <label htmlFor="appointment-queue-center">شناسه مرکز آزمایشی
    <input id="appointment-queue-center" dir="ltr" autoComplete="off"
      disabled={pending} value={center} onChange={e=>{setCenter(e.target.value);setPage(null);}}/>
   </label>
   <div className="fields">
    <label htmlFor="appointment-queue-department">شناسه تخصص برای فیلتر (اختیاری)
     <input id="appointment-queue-department" dir="ltr" autoComplete="off"
       disabled={pending} value={departmentFilter}
       onChange={e=>{setDepartmentFilter(e.target.value);setPage(null);}}/>
    </label>
    <label htmlFor="appointment-queue-doctor">شناسه پزشک برای فیلتر (اختیاری)
     <input id="appointment-queue-doctor" dir="ltr" autoComplete="off"
       disabled={pending} value={doctorFilter}
       onChange={e=>{setDoctorFilter(e.target.value);setPage(null);}}/>
    </label>
   </div>
   <div className="actions">
    <button type="button" disabled={pending}
      onClick={()=>void read('')}>خواندن صف درخواست نوبت</button>
    <button type="button" disabled={pending||!page?.hasMore||!page.nextAfterRequestRef}
      onClick={()=>{if(page?.nextAfterRequestRef)void read(
        page.nextAfterRequestRef,page.departmentRefFilter,page.requestedDoctorRefFilter);}}>
      صفحه بعد درخواست‌ها
    </button>
    <button type="button" disabled={pending||(!departmentFilter&&!doctorFilter)}
      onClick={()=>{setDepartmentFilter('');setDoctorFilter('');void read('','','');}}>
      پاک‌کردن فیلترها
    </button>
   </div>
   <p role="status" aria-live="polite" className="status">
    {pending?'در حال خواندن ژورنال آزمایشی…':status}
   </p>
   {page&&<section aria-label="درخواست‌های این صفحه">
    <p>مرکز: <code>{page.centerRef}</code> | تعداد: {page.requests.length} |
      نسخه بعدی موجود: {page.hasMore?'بله':'خیر'}</p>
    {page.requests.length===0?<p>درخواستی برای این مرکز آزمایشی ثبت نشده است.</p>:
     <ul>{page.requests.map(item=><li key={item.requestRef}>
      <article className="details">
       <h3>درخواست آزمایشی — نوبت تأیید نشده</h3>
       <p>شناسه درخواست: <code>{item.requestRef}</code> | نسخه {item.revision}</p>
       <p>بخش درخواستی: <code>{item.departmentRef}</code></p>
       <p>پزشک انتخاب‌شده توسط بیمار: <code>{item.requestedDoctorRef}</code></p>
       <p>شناسه زمان درخواستی: <code>{item.slotRef}</code></p>
       <p>پروانه پزشک: تأییدنشده | ظرفیت: بررسی‌نشده | رزرو: انجام‌نشده</p>
      </article>
     </li>)}</ul>}
   </section>}
  </section>
  <footer>TRN-TECH-217 | صف فقط‌خواندنی | بدون احراز هویت یا پذیرش واقعی</footer>
 </main>;
}
