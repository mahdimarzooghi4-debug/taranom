export type MemberAppointment = {
 centerRef: string; scheduleRevision: number; bookingRef: string;
 memberRef: string; slotRef: string; startsAt: string;
 state: 'Booked' | 'CancelledByMember';
};
declare global { interface Window { taranomSession?: { getAccessToken:()=>Promise<string>|string } } }
export class AppointmentApiError extends Error {
 readonly status:number;
 constructor(status:number,message:string){super(message);this.status=status}
}
export async function api<T>(path:string,init:RequestInit={}):Promise<T>{
 const token=await window.taranomSession?.getAccessToken();
 init.signal?.throwIfAborted();
 if(!token)throw new AppointmentApiError(401,'برای ادامه، ابتدا باید نشست ورود معتبر برقرار شود.');
 const response=await fetch(path,{...init,redirect:'error',credentials:'omit',cache:'no-store',headers:{'Accept':'application/json','Authorization':'Bearer '+token,...(init.body?{'Content-Type':'application/json'}:{}),...init.headers}});
 if(!response.ok)throw new AppointmentApiError(response.status,response.status===401?'نشست ورود معتبر نیست یا منقضی شده است.':response.status===403?'دسترسی نوبت این حساب فعال نیست.':response.status===409?'وضعیت نوبت تغییر کرده؛ فهرست را تازه کنید.':'دریافت پاسخ سرویس نوبت‌دهی ناموفق بود.');
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
export async function cancelMyAppointment(item:MemberAppointment,requestId:string=crypto.randomUUID()){
 const result=await api<{outcome:string;revision:number;appointment:{bookingRef:string;memberRef:string;centerRef:string;state:2|'CancelledByMember'}}>('/api/v1/member/members/'+encodeURIComponent(item.memberRef)+'/centers/'+encodeURIComponent(item.centerRef)+'/appointments/'+encodeURIComponent(item.bookingRef)+'/cancel',{method:'POST',body:JSON.stringify({requestId,expectedRevision:item.scheduleRevision})});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||!Number.isSafeInteger(result.revision)||!result.appointment||result.appointment.bookingRef!==item.bookingRef||result.appointment.memberRef!==item.memberRef||result.appointment.centerRef!==item.centerRef||![2,'CancelledByMember'].includes(result.appointment.state))throw new Error('نتیجه لغو نوبت قابل تأیید نیست.');
 return result;
}

export type OpenSlot={slotRef:string;centerRef:string;doctorRef:string;startsAt:string;endsAt:string;state:1|'Open'};
export type MemberSchedule={centerRef:string;revision:number;slots:OpenSlot[]};
export async function getMemberScopes(signal?:AbortSignal):Promise<string[]>{
 const result=await api<{appointmentScopes:{capability:string;scopeRef:string}[]}>('/api/v1/me/appointment-scopes',{signal});
 if(!result||!Array.isArray(result.appointmentScopes)||!result.appointmentScopes.every(s=>s&&typeof s.capability==='string'&&typeof s.scopeRef==='string'&&s.scopeRef.length>0))throw new Error('پاسخ دسترسی حساب معتبر نیست.');
 return [...new Set(result.appointmentScopes.filter(s=>s.capability==='MemberAppointmentsManage').map(s=>s.scopeRef))];
}
const memberPath=(memberRef:string,centerRef:string)=>'/api/v1/member/members/'+encodeURIComponent(memberRef)+'/centers/'+encodeURIComponent(centerRef);
export async function getMemberSchedule(memberRef:string,centerRef:string,signal?:AbortSignal):Promise<MemberSchedule>{
 const result=await api<MemberSchedule>(memberPath(memberRef,centerRef)+'/appointment-slots',{signal});
 if(!result||result.centerRef!==centerRef||!Number.isSafeInteger(result.revision)||result.revision<0||!Array.isArray(result.slots)||!result.slots.every(s=>s&&s.centerRef===centerRef&&typeof s.slotRef==='string'&&s.slotRef.length>0&&typeof s.doctorRef==='string'&&typeof s.startsAt==='string'&&typeof s.endsAt==='string'&&Number.isFinite(Date.parse(s.startsAt))&&Date.parse(s.endsAt)>Date.parse(s.startsAt)&&(s.state===1||s.state==='Open')))throw new Error('پاسخ برنامه نوبت‌دهی معتبر نیست.');
 return result;
}
export type BookingCommand={requestId:string;expectedRevision:number;slotRef:string};
export async function bookMemberAppointment(memberRef:string,centerRef:string,command:BookingCommand){
 const result=await api<{outcome:string;revision:number;appointment:{bookingRef:string;memberRef:string;centerRef:string;slotRef:string}}>(memberPath(memberRef,centerRef)+'/appointments',{method:'POST',body:JSON.stringify(command)});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||!Number.isSafeInteger(result.revision)||result.revision<0||!result.appointment||typeof result.appointment.bookingRef!=='string'||!result.appointment.bookingRef||result.appointment.memberRef!==memberRef||result.appointment.centerRef!==centerRef||result.appointment.slotRef!==command.slotRef)throw new Error('نتیجه ثبت نوبت قابل تأیید نیست.');
 return result.appointment;
}

export type MemberProfile={revision:number;fullName:string;birthDate:string|null;city:string};
export async function getMyProfile(signal?:AbortSignal){
 const result=await api<{profile:MemberProfile}>('/api/v1/me/profile',{signal});
 const p=result?.profile;
 if(!p||!Number.isSafeInteger(p.revision)||p.revision<0||typeof p.fullName!=='string'||typeof p.city!=='string'||!(p.birthDate===null||typeof p.birthDate==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(p.birthDate)&&Number.isFinite(Date.parse(p.birthDate))))throw new Error('پاسخ مشخصات فردی معتبر نیست.');
 return p;
}
export async function saveMyProfile(profile:MemberProfile,requestId:string){
 const result=await api<{outcome:string;revision:number}>('/api/v1/me/profile',{method:'PUT',body:JSON.stringify({requestId,expectedRevision:profile.revision,fullName:profile.fullName,birthDate:profile.birthDate,city:profile.city})});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||!Number.isSafeInteger(result.revision)||result.revision<=profile.revision)throw new Error('نتیجه ذخیره مشخصات قابل تأیید نیست.');
 return result;
}

export type SupportTopic='account'|'group'|'appointments'|'privacy';
export type SupportCommand={requestId:string;topic:SupportTopic;message:string};
export type SupportRequest=SupportCommand&{requestRef:string;state:'Registered'|'Answered';createdAt:string;revision:number;reply:string|null;repliedAt:string|null};
export type SupportPage={requests:SupportRequest[];nextBefore:string|null};
const validSupport=(r:SupportRequest)=>r&&typeof r.requestRef==='string'&&r.requestRef.length>0&&typeof r.requestId==='string'&&['account','group','appointments','privacy'].includes(r.topic)&&typeof r.message==='string'&&((r.state==='Registered'&&r.revision===0&&r.reply===null&&r.repliedAt===null)||(r.state==='Answered'&&r.revision===1&&typeof r.reply==='string'&&r.reply.trim().length>=10&&typeof r.repliedAt==='string'&&Number.isFinite(Date.parse(r.repliedAt))))&&typeof r.createdAt==='string'&&Number.isFinite(Date.parse(r.createdAt));
export async function getMySupportRequests(options:{before?:string;requestId?:string}={},signal?:AbortSignal):Promise<SupportPage>{
 const query=new URLSearchParams(options);
 const result=await api<SupportPage>('/api/v1/me/support-requests'+(query.size?'?'+query:''),{signal});
 if(!result||!Array.isArray(result.requests)||!result.requests.every(validSupport)||!(result.nextBefore===null||typeof result.nextBefore==='string'&&/^[1-9]\d*$/.test(result.nextBefore)))throw new Error('پاسخ پیگیری درخواست معتبر نیست.');
 return result;
}
export async function createMySupportRequest(command:SupportCommand){
 const result=await api<{outcome:string;request:SupportRequest}>('/api/v1/me/support-requests',{method:'POST',body:JSON.stringify(command)});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||!validSupport(result.request)||result.request.requestId!==command.requestId||result.request.topic!==command.topic||result.request.message!==command.message.trim())throw new Error('ثبت درخواست قابل تأیید نیست.');
 return result.request;
}

/** Current schedule-derived center references only; not verified provider identities. */
export type AvailableAppointmentCenter={
 centerRef:string;revision:number;openSlotCount:number;nextAvailableAt:string;
};
export async function getAvailableAppointmentCenters(signal?:AbortSignal):Promise<AvailableAppointmentCenter[]>{
 const response=await api<{centers:AvailableAppointmentCenter[];publicationGateRequired?:boolean}>('/api/v1/me/available-appointment-centers',{signal});
 if(!response||response.publicationGateRequired!==true||!Array.isArray(response.centers)||response.centers.length!==0||
   !response.centers.every(center=>center&&typeof center.centerRef==='string'&&
    center.centerRef.trim().length>0&&Number.isSafeInteger(center.revision)&&center.revision>=0&&
    Number.isSafeInteger(center.openSlotCount)&&center.openSlotCount>0&&
    typeof center.nextAvailableAt==='string'&&Number.isFinite(Date.parse(center.nextAvailableAt)))||
   new Set(response.centers.map(center=>center.centerRef)).size!==response.centers.length)
  throw new Error('فهرست مراکز بدون مجوز انتشار قابل استفاده نیست.');
 return response.centers;
}

export type MemberNotification={notificationRef:string;sourceRequestId:string;kind:'SupportAnswered';createdAt:string;readAt:string|null};
export type NotificationPage={notifications:MemberNotification[];unreadCount:number;nextBefore:string|null};
const notificationId=(value:unknown)=>typeof value==='string'&&/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value);
const validNotification=(n:MemberNotification)=>n&&notificationId(n.notificationRef)&&notificationId(n.sourceRequestId)&&n.kind==='SupportAnswered'&&typeof n.createdAt==='string'&&Number.isFinite(Date.parse(n.createdAt))&&(n.readAt===null||typeof n.readAt==='string'&&Number.isFinite(Date.parse(n.readAt)));
export async function getMyNotifications(options:{before?:string;notificationRef?:string}={},signal?:AbortSignal):Promise<NotificationPage>{
 const query=new URLSearchParams(options),result=await api<NotificationPage>('/api/v1/me/notifications'+(query.size?'?'+query:''),{signal});
 if(!result||!Array.isArray(result.notifications)||!result.notifications.every(validNotification)||new Set(result.notifications.map(n=>n.notificationRef)).size!==result.notifications.length||!Number.isSafeInteger(result.unreadCount)||result.unreadCount<0||!(result.nextBefore===null||typeof result.nextBefore==='string'&&/^[1-9]\d*$/.test(result.nextBefore)))throw new Error('پاسخ اعلان‌ها معتبر نیست.');
 return result;
}
export async function markMyNotificationRead(notificationRef:string){
 const result=await api<{notificationRef:string;readAt:string}>('/api/v1/me/notifications/'+encodeURIComponent(notificationRef)+'/read',{method:'POST'});
 if(!result||result.notificationRef!==notificationRef||typeof result.readAt!=='string'||!Number.isFinite(Date.parse(result.readAt)))throw new Error('خواندن اعلان هنوز قابل تأیید نیست.');return result;
}

export type GroupDraftMethod='NewRegistration'|'ExistingMember';
export type GroupDraftCommand={requestId:string;method:GroupDraftMethod;recipientLabel:string};
export type GroupDraft=GroupDraftCommand&{draftRef:string;state:'Draft'|'Withdrawn';createdAt:string;withdrawnAt:string|null};
export type GroupDraftPage={drafts:GroupDraft[];nextBefore:string|null};
const validGroupDraft=(d:GroupDraft)=>d&&notificationId(d.draftRef)&&notificationId(d.requestId)&&['NewRegistration','ExistingMember'].includes(d.method)&&typeof d.recipientLabel==='string'&&d.recipientLabel.trim().length>=2&&typeof d.createdAt==='string'&&Number.isFinite(Date.parse(d.createdAt))&&((d.state==='Draft'&&d.withdrawnAt===null)||(d.state==='Withdrawn'&&typeof d.withdrawnAt==='string'&&Number.isFinite(Date.parse(d.withdrawnAt))));
export async function getMyGroupDrafts(options:{before?:string;requestId?:string;draftRef?:string}={},signal?:AbortSignal):Promise<GroupDraftPage>{
 const query=new URLSearchParams(options),result=await api<GroupDraftPage>('/api/v1/me/group-invitation-drafts'+(query.size?'?'+query:''),{signal});
 if(!result||!Array.isArray(result.drafts)||!result.drafts.every(validGroupDraft)||!(result.nextBefore===null||typeof result.nextBefore==='string'&&/^[1-9]\d*$/.test(result.nextBefore)))throw new Error('پاسخ پیش‌نویس‌های دعوت معتبر نیست.');return result;
}
export async function createMyGroupDraft(command:GroupDraftCommand){
 const result=await api<{outcome:string;draft:GroupDraft}>('/api/v1/me/group-invitation-drafts',{method:'POST',body:JSON.stringify(command)});
 if(!result||!['Applied','Replayed'].includes(result.outcome)||!validGroupDraft(result.draft)||result.draft.requestId!==command.requestId||result.draft.method!==command.method||result.draft.recipientLabel!==command.recipientLabel.trim())throw new Error('ذخیرهٔ پیش‌نویس هنوز قابل تأیید نیست.');return result.draft;
}
export async function withdrawMyGroupDraft(draftRef:string){
 const result=await api<{draft:GroupDraft}>('/api/v1/me/group-invitation-drafts/'+encodeURIComponent(draftRef)+'/withdraw',{method:'POST'});
 if(!result||!validGroupDraft(result.draft)||result.draft.draftRef!==draftRef||result.draft.state!=='Withdrawn')throw new Error('بایگانی پیش‌نویس هنوز قابل تأیید نیست.');return result.draft;
}

async function invitationApi<T>(path:string,init:RequestInit={}):Promise<T>{
 try{return await api<T>(path,init)}catch(e){if(e instanceof AppointmentApiError&&e.status!==401){const message=e.status===403?'دسترسی این حساب به دعوت مجاز نیست.':e.status===404?'دعوت پیدا نشد یا در دسترس این حساب نیست.':e.status===409?'وضعیت دعوت تغییر کرده یا پیوند دیگر معتبر نیست. دوباره وضعیت را بررسی کنید.':e.status===400?'اطلاعات دعوت معتبر نیست.':'دریافت نتیجهٔ دعوت ناموفق بود.';throw new AppointmentApiError(e.status,message)}throw e}
}
export type GroupInvitationState='Pending'|'Accepted'|'Declined'|'Revoked'|'Expired';
export type GroupInvitation={invitationRef:string;requestId?:string;state:GroupInvitationState;createdAt:string;expiresAt:string;respondedAt:string|null};
const validInvitation=(v:GroupInvitation)=>v&&notificationId(v.invitationRef)&&['Pending','Accepted','Declined','Revoked','Expired'].includes(v.state)&&typeof v.createdAt==='string'&&Number.isFinite(Date.parse(v.createdAt))&&typeof v.expiresAt==='string'&&Date.parse(v.expiresAt)>Date.parse(v.createdAt)&&(['Accepted','Declined'].includes(v.state)?typeof v.respondedAt==='string'&&Number.isFinite(Date.parse(v.respondedAt)):v.respondedAt===null);
const invitationPath=(draftRef:string)=>'/api/v1/me/group-invitation-drafts/'+encodeURIComponent(draftRef)+'/invitations';
export async function getMyGroupInvitation(draftRef:string,requestId?:string,signal?:AbortSignal){
 const r=await invitationApi<{invitation:GroupInvitation|null}>(invitationPath(draftRef)+(requestId?'?requestId='+encodeURIComponent(requestId):''),{signal});
 if(!r||!(r.invitation===null||validInvitation(r.invitation)&&notificationId(r.invitation.requestId)&&(!requestId||r.invitation.requestId===requestId)))throw new Error('وضعیت دعوت قابل تأیید نیست.');return r.invitation;
}
export async function issueMyGroupInvitation(draftRef:string,command:{requestId:string;token:string}){
 const r=await invitationApi<{outcome:string;invitation:GroupInvitation}>(invitationPath(draftRef),{method:'POST',body:JSON.stringify(command)});
 if(!r||!['Applied','Replayed'].includes(r.outcome)||!validInvitation(r.invitation)||r.invitation.requestId!==command.requestId)throw new Error('ساخت پیوند قابل تأیید نیست.');return r.invitation;
}
export async function inspectGroupInvitation(token:string,decision?:'Accepted'|'Declined'){
 const r=await invitationApi<{invitation:GroupInvitation;consentVersion:string}>('/api/v1/me/group-invitations/'+(decision?'respond':'preview'),{method:'POST',body:JSON.stringify(decision?{token,decision,consentVersion:'group-invitation-v1'}:{token})});
 if(!r||r.consentVersion!=='group-invitation-v1'||!validInvitation(r.invitation)||(decision&&r.invitation.state!==decision))throw new Error('نتیجهٔ پاسخ به دعوت قابل تأیید نیست.');return r.invitation;
}


export type GroupInvitationHistoryPage={invitations:GroupInvitation[];nextBefore:string|null};
export async function getMyInvitationHistory(options:{before?:string;invitationRef?:string}={},signal?:AbortSignal):Promise<GroupInvitationHistoryPage>{
 const query=new URLSearchParams(options),r=await invitationApi<GroupInvitationHistoryPage>('/api/v1/me/group-invitations'+(query.size?'?'+query:''),{signal});
 if(!r||!Array.isArray(r.invitations)||r.invitations.length>20||!r.invitations.every(v=>validInvitation(v)&&['Accepted','Declined'].includes(v.state)&&v.requestId===undefined&&(!options.invitationRef||v.invitationRef===options.invitationRef))||new Set(r.invitations.map(v=>v.invitationRef)).size!==r.invitations.length||!(r.nextBefore===null||typeof r.nextBefore==='string'&&/^[1-9]\d*$/.test(r.nextBefore)))throw new Error('پاسخ سابقهٔ دعوت‌ها معتبر نیست.');
 return r;
}
