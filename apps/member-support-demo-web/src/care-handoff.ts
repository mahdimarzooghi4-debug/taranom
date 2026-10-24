// TRN-TECH-198: a human-carried locator for one synthetic, unverified
// coordination case. Never a bearer token, consent, identity or referral.
const FORMAT='TRN-TECH-198-TEST-CARE-HANDOFF' as const;
const ref=(x:unknown):x is string=>typeof x==='string'&&
 x.length<=100&&/^test:[A-Za-z0-9_:-]+$/.test(x);
const uuid=(x:unknown):x is string=>typeof x==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(x);
export type CareHandoff={
 format:typeof FORMAT;version:1;caseRef:string;memberRef:string;
 originalMemberRequestRef:string;rootRequestId:string;
 revision:number;lastEventRequestId:string
};
type SourceLike={
 caseRef:string;revision:number;
 events:{requestId:string;memberRef:string;originalMemberRequestRef:string}[]
};
const keys=['format','version','caseRef','memberRef',
 'originalMemberRequestRef','rootRequestId','revision','lastEventRequestId'];
export function createCareHandoff(source:SourceLike|null,refInput:string,
 memberInput:string):string{
 if(!source||source.revision<1||source.events.length!==source.revision||
    source.caseRef!==refInput||!ref(source.caseRef))return '';
 const first=source.events[0],last=source.events[source.revision-1];
 if(!first||!last||!ref(first.memberRef)||!ref(first.originalMemberRequestRef)||
    first.memberRef!==memberInput||!uuid(first.requestId)||!uuid(last.requestId))return '';
 const ticket:CareHandoff={format:FORMAT,version:1,
  caseRef:source.caseRef,memberRef:first.memberRef,
  originalMemberRequestRef:first.originalMemberRequestRef,
  rootRequestId:first.requestId,revision:source.revision,
  lastEventRequestId:last.requestId};
 return JSON.stringify(ticket);
}
export function parseCareHandoff(text:string):CareHandoff|null{
 if(text.length===0||text.length>1024)return null;
 try{
  const raw:unknown=JSON.parse(text);
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return null;
  const x=raw as Record<string,unknown>;
  if(Object.keys(x).length!==keys.length||
     Object.keys(x).some(k=>!keys.includes(k))||
     x.format!==FORMAT||x.version!==1||
     !ref(x.caseRef)||!ref(x.memberRef)||
     !ref(x.originalMemberRequestRef)||!uuid(x.rootRequestId)||
     !uuid(x.lastEventRequestId)||
     !Number.isSafeInteger(x.revision)||Number(x.revision)<1)return null;
  return x as CareHandoff;
 }catch{return null;}
}
export function matchesCareHandoff(source:SourceLike,ticket:CareHandoff):boolean{
 if(source.caseRef!==ticket.caseRef||source.revision!==ticket.revision||
    source.events.length!==ticket.revision)return false;
 const root=source.events[0],last=source.events[ticket.revision-1];
 return !!root&&!!last&&root.memberRef===ticket.memberRef&&
   root.originalMemberRequestRef===ticket.originalMemberRequestRef&&
   root.requestId===ticket.rootRequestId&&
   last.requestId===ticket.lastEventRequestId;
}
