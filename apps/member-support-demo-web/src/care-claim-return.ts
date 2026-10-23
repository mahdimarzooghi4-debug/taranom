// TRN-TECH-199 — exact last unverified synthetic care claim reference.
// Deliberately NOT a received message, health record, consent or credential.
export const RETURN_FORMAT='TRN-TECH-199-TEST-CARE-CLAIM-RETURN' as const;
type ClaimAction='RecordUnverifiedCoordinatorContact'|
 'AttachUnverifiedClinicalSourceReference';
export type CareClaimReturn={
 format:typeof RETURN_FORMAT;version:1;
 caseRef:string;memberRef:string;originalMemberRequestRef:string;
 rootRequestId:string;revision:number;claimRequestId:string;
 claimAction:ClaimAction;claimedActorRef:string;claimRef:string
};
type EventLike={
 requestId:string;action:string;memberRef:string;
 originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:string|null;unverifiedNoteOrDocumentRef:string|null
};
type JournalLike={caseRef:string;revision:number;events:EventLike[]};
const keys=['format','version','caseRef','memberRef','originalMemberRequestRef',
 'rootRequestId','revision','claimRequestId','claimAction',
 'claimedActorRef','claimRef'];
const synthetic=(v:unknown):v is string=>typeof v==='string'&&
 v.length<=100&&/^test:[A-Za-z0-9_:-]+$/.test(v);
const uuid4=(v:unknown):v is string=>typeof v==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);
const claim=(v:unknown):v is ClaimAction=>
 v==='RecordUnverifiedCoordinatorContact'||
 v==='AttachUnverifiedClinicalSourceReference';
export function createCareClaimReturn(source:JournalLike|null,selectedRef:string):string{
 if(!source||source.caseRef!==selectedRef||!synthetic(source.caseRef)||
  !Number.isSafeInteger(source.revision)||source.revision<2||
  source.events.length!==source.revision)return '';
 const first=source.events[0],last=source.events[source.revision-1];
 if(!first||!last||first.action!=='RequestNonclinicalCoordination'||
  !claim(last.action)||!uuid4(first.requestId)||!uuid4(last.requestId)||
  !synthetic(first.memberRef)||!synthetic(first.originalMemberRequestRef)||
  !synthetic(last.claimedActorRef)||!synthetic(last.unverifiedNoteOrDocumentRef)||
  last.memberRef!==first.memberRef||
  last.originalMemberRequestRef!==first.originalMemberRequestRef||
  last.originalSubmissionRequestId!==first.requestId)return '';
 const locator:CareClaimReturn={
  format:RETURN_FORMAT,version:1,caseRef:source.caseRef,
  memberRef:first.memberRef,originalMemberRequestRef:first.originalMemberRequestRef,
  rootRequestId:first.requestId,revision:source.revision,
  claimRequestId:last.requestId,claimAction:last.action,
  claimedActorRef:last.claimedActorRef,claimRef:last.unverifiedNoteOrDocumentRef
 };
 return JSON.stringify(locator);
}
export function parseCareClaimReturn(text:string):CareClaimReturn|null{
 if(text.length===0||text.length>1400)return null;
 try{
  const raw:unknown=JSON.parse(text);
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return null;
  const x=raw as Record<string,unknown>;
  if(Object.keys(x).length!==keys.length||
     Object.keys(x).some(k=>!keys.includes(k))||
     x.format!==RETURN_FORMAT||x.version!==1||
     !synthetic(x.caseRef)||!synthetic(x.memberRef)||
     !synthetic(x.originalMemberRequestRef)||!uuid4(x.rootRequestId)||
     !Number.isSafeInteger(x.revision)||Number(x.revision)<2||
     !uuid4(x.claimRequestId)||x.claimRequestId===x.rootRequestId||
     !claim(x.claimAction)||!synthetic(x.claimedActorRef)||
     !synthetic(x.claimRef))return null;
  return x as CareClaimReturn;
 }catch{return null;}
}
export function matchesCareClaimReturn(source:JournalLike,locator:CareClaimReturn):boolean{
 if(source.caseRef!==locator.caseRef||
    source.revision!==locator.revision||
    source.events.length!==locator.revision)return false;
 const first=source.events[0],last=source.events[source.revision-1];
 return !!first&&!!last&&
   first.action==='RequestNonclinicalCoordination'&&
   first.memberRef===locator.memberRef&&
   first.originalMemberRequestRef===locator.originalMemberRequestRef&&
   first.requestId===locator.rootRequestId&&
   last.originalSubmissionRequestId===locator.rootRequestId&&
   last.memberRef===locator.memberRef&&
   last.originalMemberRequestRef===locator.originalMemberRequestRef&&
   last.requestId===locator.claimRequestId&&
   last.action===locator.claimAction&&
   last.claimedActorRef===locator.claimedActorRef&&
   last.unverifiedNoteOrDocumentRef===locator.claimRef;
}
