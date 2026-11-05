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