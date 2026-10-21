// TRN-TECH-184: CI-only source-linked coordination claims. Not a clinician
// login, patient referral, treatment, appointment or approved contact.
export type Action='RequestNonclinicalCoordination'|
 'RecordUnverifiedCoordinatorContact'|'AttachUnverifiedClinicalSourceReference';
export type Event={revision:number;action:Action;requestId:string;memberRef:string;
 originalMemberRequestRef:string;originalSubmissionRequestId:string;
 claimedActorRef:string|null;unverifiedNoteOrDocumentRef:string|null};
export type Snapshot={contract:'TRN-TECH-115';syntheticOnly:true;unverifiedClaimsOnly:true;
 caseRef:string;outcome:'READ'|'RecordedUnverified'|'Replayed';
 revision:number;events:Event[];
 realMemberAuthenticated:false;realCoordinatorVerified:false;
 clinicalSourceVerified:false;clinicianAuthorized:false;
 referralOrdered:false;referralDelivered:false;appointmentBooked:false;
 treatmentStarted:false;medicalRecordAccessGranted:false;
 emergencyServiceDispatched:false;consentGranted:false;
 walletMutated:false;nationalReleaseAllowed:false};

const deny=['realMemberAuthenticated','realCoordinatorVerified',
 'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
 'referralDelivered','appointmentBooked','treatmentStarted',
 'medicalRecordAccessGranted','emergencyServiceDispatched','consentGranted',
 'walletMutated','nationalReleaseAllowed'] as const;
export const testRef=(x:unknown):x is string=>typeof x==='string'&&
 x.length<=100&&/^test:[A-Za-z0-9_:-]+$/.test(x);
export const uuid=(x:unknown):x is string=>typeof x==='string'&&
 /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(x);
const ZERO='00000000-0000-0000-0000-000000000000';
export function valid(x:unknown,ref:string):x is Snapshot{
 if(!x||typeof x!=='object'||Array.isArray(x))return false;
 const s=x as Partial<Snapshot>;
 if(s.contract!=='TRN-TECH-115'||s.syntheticOnly!==true||
 s.unverifiedClaimsOnly!==true||s.caseRef!==ref||
 !['READ','RecordedUnverified','Replayed'].includes(String(s.outcome))||
 !Number.isSafeInteger(s.revision)||s.revision!<0||
 !Array.isArray(s.events)||s.events.length!==s.revision||
 !deny.every(k=>s[k]===false))return false;
 let first:Event|undefined;
 for(const [i,event] of s.events.entries()){
  if(!event||event.revision!==i+1||!uuid(event.requestId)||
    !testRef(event.memberRef)||!testRef(event.originalMemberRequestRef))
   return false;
  if(i===0){
   if(event.action!=='RequestNonclinicalCoordination'||
     event.originalSubmissionRequestId!==ZERO||
     event.claimedActorRef!==null||event.unverifiedNoteOrDocumentRef!==null)
    return false;
   first=event;
  }else if(!first||
   !['RecordUnverifiedCoordinatorContact','AttachUnverifiedClinicalSourceReference'].includes(event.action)||
   event.memberRef!==first.memberRef||
   event.originalMemberRequestRef!==first.originalMemberRequestRef||
   event.originalSubmissionRequestId!==first.requestId||
   !testRef(event.claimedActorRef)||!testRef(event.unverifiedNoteOrDocumentRef))
   return false;
 }
 return new Set(s.events.map(e=>e.requestId)).size===s.events.length;
}
