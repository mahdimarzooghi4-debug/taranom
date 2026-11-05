export type MemberAppointment={centerRef:string;scheduleRevision:number;bookingRef:string;memberRef:string;slotRef:string;startsAt:string;state:string};
declare global { interface Window { taranomSession?: { getAccessToken:()=>Promise<string>|string } } }
async function api<T>(path:string,init:RequestInit={}):Promise<T>{
 const token=await window.taranomSession?.getAccessToken();
 if(!token)throw new Error('برای دیدن نوبت‌ها، ابتدا باید نشست ورود معتبر برقرار شود.');
 const response=await fetch(path,{...init,credentials:'omit',cache:'no-store',headers:{'Accept':'application/json','Authorization':'Bearer '+token,...(init.body?{'Content-Type':'application/json'}:{}),...init.headers}});
 if(!response.ok)throw new Error(response.status===401?'نشست ورود معتبر نیست یا منقضی شده است.':response.status===403?'دسترسی نوبت این حساب فعال نیست.':response.status===409?'وضعیت نوبت تغییر کرده؛ فهرست را تازه کنید.':'سرویس نوبت‌دهی پاسخ '+response.status+' داد.');
 return await response.json() as T;
}
export const getMyAppointments=()=>api<{appointments:MemberAppointment[]}>('/api/v1/me/appointments');
export const cancelMyAppointment=(item:MemberAppointment)=>api('/api/v1/member/members/'+encodeURIComponent(item.memberRef)+'/centers/'+encodeURIComponent(item.centerRef)+'/appointments/'+encodeURIComponent(item.bookingRef)+'/cancel',{method:'POST',body:JSON.stringify({requestId:crypto.randomUUID(),expectedRevision:item.scheduleRevision})});