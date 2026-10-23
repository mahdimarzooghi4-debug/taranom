// TRN-TECH-196. Read-only pinned care history for the Center synthetic QA.
// Trust requires the existing complete journal snapshot and all source fields.
import {type Event,type Snapshot} from './care-journal-source';
export const CARE_HISTORY_LIMIT=4;
export type CareHistoryPage={
 contract:'TRN-TECH-195';syntheticOnly:true;unverifiedClaimsOnly:true;
 source:'VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL';
 caseRef:string;currentRevision:number;expectedCurrentRevision:number;
 afterRevision:number;limit:number;hasMore:boolean;nextAfterRevision:number|null;
 events:Event[];
 realMemberAuthenticated:false;realCoordinatorVerified:false;
 clinicalSourceVerified:false;clinicianAuthorized:false;
 referralOrdered:false;referralDelivered:false;appointmentBooked:false;
 treatmentStarted:false;medicalRecordAccessGranted:false;
 emergencyServiceDispatched:false;consentGranted:false;
 walletMutated:false;nationalReleaseAllowed:false;
};
const denials=['realMemberAuthenticated','realCoordinatorVerified',
 'clinicalSourceVerified','clinicianAuthorized','referralOrdered',
 'referralDelivered','appointmentBooked','treatmentStarted',
 'medicalRecordAccessGranted','emergencyServiceDispatched','consentGranted',
 'walletMutated','nationalReleaseAllowed'] as const;
const exactEvent=(a:Event,b:Event)=>
 a.revision===b.revision&&a.action===b.action&&a.requestId===b.requestId&&
 a.memberRef===b.memberRef&&
 a.originalMemberRequestRef===b.originalMemberRequestRef&&
 a.originalSubmissionRequestId===b.originalSubmissionRequestId&&
 a.claimedActorRef===b.claimedActorRef&&
 a.unverifiedNoteOrDocumentRef===b.unverifiedNoteOrDocumentRef;
// An independently fetched server page cannot alter the verified case or
// make a completed source history appear incomplete/more complete.
export function validHistoryPage(raw:unknown,pin:Snapshot,after:number):
 raw is CareHistoryPage{
 if(!raw||typeof raw!=='object'||Array.isArray(raw)||
    !Number.isSafeInteger(after)||after<0||after>pin.revision)return false;
 const page=raw as Partial<CareHistoryPage>;
 if(page.contract!=='TRN-TECH-195'||page.syntheticOnly!==true||
    page.unverifiedClaimsOnly!==true||
    page.source!=='VERIFIED_ISOLATED_POSTGRES_CARE_JOURNAL'||
    page.caseRef!==pin.caseRef||
    page.currentRevision!==pin.revision||
    page.expectedCurrentRevision!==pin.revision||
    page.afterRevision!==after||page.limit!==CARE_HISTORY_LIMIT||
    typeof page.hasMore!=='boolean'||!Array.isArray(page.events)||
    !denials.every(k=>page[k]===false))return false;
 const count=Math.min(CARE_HISTORY_LIMIT,pin.revision-after);
 if(page.events.length!==count)return false;
 for(const [index,event] of page.events.entries()){
  const saved=pin.events[after+index];
  if(!saved||!event||!exactEvent(event,saved)||
    event.revision!==after+index+1)return false;
 }
 const more=after+count<pin.revision;
 return page.hasMore===more&&
    page.nextAfterRevision===(more?after+count:null);
}
