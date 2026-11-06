export type CenterScope = { capability: string; scopeRef: string };
export type AppointmentSlot = { slotRef:string; doctorRef:string; startsAt:string; endsAt:string; state:string };
export type CenterBooking = { bookingRef:string; memberRef:string; slotRef:string; startsAt:string; state:string };
export type CenterSchedule = { centerRef:string; revision:number; slots:AppointmentSlot[] };
export type CenterAppointments = { centerRef:string; revision:number; appointments:CenterBooking[] };
declare global { interface Window { taranomSession?: { getAccessToken:()=>Promise<string>|string } } }
async function api<T>(path:string, init:RequestInit={}):Promise<T>{
  const token=await window.taranomSession?.getAccessToken();
  if(!token) throw new Error('نشست ورود معتبر در این محیط در دسترس نیست.');
  const response=await fetch(path,{...init,redirect:'error',credentials:'omit',cache:'no-store',headers:{'Accept':'application/json','Authorization':'Bearer '+token,...(init.body?{'Content-Type':'application/json'}:{}),...init.headers}});
  if(!response.ok) throw new Error(response.status===401?'نشست ورود معتبر نیست یا منقضی شده است.':response.status===403?'برای این مرکز دسترسی فعال ندارید.':response.status===409?'اطلاعات برنامه تغییر کرده؛ دوباره بارگذاری کنید.':'سرویس نوبت‌دهی پاسخ '+response.status+' داد.');
  return await response.json() as T;
}
export const getAppointmentScopes=()=>api<{appointmentScopes:CenterScope[]}>('/api/v1/me/appointment-scopes');
export const getCenterSchedule=(centerRef:string)=>api<CenterSchedule>('/api/v1/centers/'+encodeURIComponent(centerRef)+'/appointment-slots');
export const getCenterAppointments=(centerRef:string)=>api<CenterAppointments>('/api/v1/centers/'+encodeURIComponent(centerRef)+'/appointments');
export const defineCenterSlot=(centerRef:string,payload:{requestId:string;expectedRevision:number;slotRef:string;doctorRef:string;startsAt:string;endsAt:string})=>api('/api/v1/centers/'+encodeURIComponent(centerRef)+'/appointment-slots',{method:'POST',body:JSON.stringify(payload)});
export type DirectoryScope={capability:'CenterAccessGrantsManage'|'CenterRosterManage';scopeRef:string};
export type CenterDraftProfile={revision:number;displayName:string;city:string};
export type DoctorDraft={doctorRef:string;revision:number;displayName:string;specialty:string;verificationState:'UNVERIFIED_DRAFT'};
export type DoctorDraftPage={centerRef:string;doctors:DoctorDraft[];nextAfter:string|null};
const validText=(s:unknown,max:number)=>typeof s==='string'&&s.trim().length>0&&s.length<=max;
export async function getDirectoryScopes(signal?:AbortSignal):Promise<DirectoryScope[]>{
 const response=await api<{scopes:DirectoryScope[]}>('/api/v1/me/center-directory-draft-scopes',{signal});
 if(!response||!Array.isArray(response.scopes)||!response.scopes.every(s=>s&&
  ['CenterAccessGrantsManage','CenterRosterManage'].includes(s.capability)&&validText(s.scopeRef,256)))
  throw new Error('پاسخ مجوز مرکز معتبر نیست.');
 return response.scopes;
}
export async function getCenterDirectoryDraft(centerRef:string,signal?:AbortSignal){
 const response=await api<{centerRef:string;profile:CenterDraftProfile;verificationState:'UNVERIFIED_DRAFT'}>(
  '/api/v1/centers/'+encodeURIComponent(centerRef)+'/directory-draft',{signal});
 if(!response||response.centerRef!==centerRef||response.verificationState!=='UNVERIFIED_DRAFT'||
  !response.profile||!Number.isSafeInteger(response.profile.revision)||response.profile.revision<0||
  typeof response.profile.displayName!=='string'||typeof response.profile.city!=='string')
  throw new Error('پاسخ مشخصات مرکز معتبر نیست.');
 return response.profile;
}
export async function saveCenterDirectoryDraft(centerRef:string,command:{requestId:string;expectedRevision:number;displayName:string;city:string}){
 const result=await api<{outcome:string;revision:number;verificationState:string}>(
  '/api/v1/centers/'+encodeURIComponent(centerRef)+'/directory-draft',
  {method:'PUT',body:JSON.stringify(command)});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||result.verificationState!=='UNVERIFIED_DRAFT'||
   !Number.isSafeInteger(result.revision)||result.revision!==command.expectedRevision+1)
  throw new Error('رسید ذخیره مرکز معتبر نیست.');
 return result;
}
export async function getDoctorDirectoryDrafts(centerRef:string,after?:string,signal?:AbortSignal):Promise<DoctorDraftPage>{
 const params=after?'?after='+encodeURIComponent(after):'';
 const result=await api<DoctorDraftPage>('/api/v1/centers/'+encodeURIComponent(centerRef)+'/doctor-drafts'+params,{signal});
 if(!result||result.centerRef!==centerRef||!Array.isArray(result.doctors)||result.doctors.length>25||
  !result.doctors.every(d=>d&&validText(d.doctorRef,256)&&Number.isSafeInteger(d.revision)&&d.revision>0&&
   typeof d.displayName==='string'&&typeof d.specialty==='string'&&d.verificationState==='UNVERIFIED_DRAFT')||
  !(result.nextAfter===null||typeof result.nextAfter==='string'))
  throw new Error('فهرست پزشکان اعلامی معتبر نیست.');
 return result;
}
export async function saveDoctorDirectoryDraft(centerRef:string,doctorRef:string,command:{requestId:string;expectedRevision:number;displayName:string;specialty:string}){
 const result=await api<{outcome:string;revision:number;verificationState:string}>(
  '/api/v1/centers/'+encodeURIComponent(centerRef)+'/doctor-drafts/'+encodeURIComponent(doctorRef),
  {method:'PUT',body:JSON.stringify(command)});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||result.verificationState!=='UNVERIFIED_DRAFT'||
  !Number.isSafeInteger(result.revision)||result.revision!==command.expectedRevision+1)
  throw new Error('رسید ثبت پزشک اعلامی معتبر نیست.');
 return result;
}
