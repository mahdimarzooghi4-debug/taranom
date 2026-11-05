export type MemberAppointment = {
 centerRef: string; scheduleRevision: number; bookingRef: string;
 memberRef: string; slotRef: string; startsAt: string;
 state: 'Booked' | 'CancelledByMember';
};
declare global { interface Window { taranomSession?: { getAccessToken:()=>Promise<string>|string } } }
async function api<T>(path:string,init:RequestInit={}):Promise<T>{
 const token=await window.taranomSession?.getAccessToken();
 init.signal?.throwIfAborted();
 if(!token)throw new Error('برای دیدن نوبت‌ها، ابتدا باید نشست ورود معتبر برقرار شود.');
 const response=await fetch(path,{...init,redirect:'error',credentials:'omit',cache:'no-store',headers:{'Accept':'application/json','Authorization':'Bearer '+token,...(init.body?{'Content-Type':'application/json'}:{}),...init.headers}});
 if(!response.ok)throw new Error(response.status===401?'نشست ورود معتبر نیست یا منقضی شده است.':response.status===403?'دسترسی نوبت این حساب فعال نیست.':response.status===409?'وضعیت نوبت تغییر کرده؛ فهرست را تازه کنید.':'دریافت پاسخ سرویس نوبت‌دهی ناموفق بود.');
 return await response.json() as T;
}
export async function getMyAppointments(signal?:AbortSignal){
 const response=await api<{appointments:MemberAppointment[]}>('/api/v1/me/appointments',{signal});
 if(!response || !Array.isArray(response.appointments) || !response.appointments.every(item =>
   item && ['centerRef','memberRef','slotRef','bookingRef'].every(key => typeof item[key as keyof MemberAppointment]==='string') &&
   Number.isSafeInteger(item.scheduleRevision) && item.scheduleRevision>=0 &&
   typeof item.startsAt==='string' && Number.isFinite(Date.parse(item.startsAt)) &&
   ['Booked','CancelledByMember'].includes(item.state))) throw new Error('پاسخ نوبت‌دهی معتبر نیست. دوباره تلاش کنید.');
 return response;
}
export const cancelMyAppointment=(item:MemberAppointment,requestId:string=crypto.randomUUID())=>api('/api/v1/member/members/'+encodeURIComponent(item.memberRef)+'/centers/'+encodeURIComponent(item.centerRef)+'/appointments/'+encodeURIComponent(item.bookingRef)+'/cancel',{method:'POST',body:JSON.stringify({requestId,expectedRevision:item.scheduleRevision})});
