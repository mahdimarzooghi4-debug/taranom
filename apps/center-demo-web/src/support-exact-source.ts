// TRN-TECH-202: a plausible POST ACK is not a committed event.
type Cmd={requestId:string;expectedRevision:number;action:'SUBMIT_UNVERIFIED'|'FOLLOW_UP_UNVERIFIED';memberRef:string;topic:'MEDICAL_SERVICE_COMPLAINT'|'INSURANCE_COVERAGE_DISPUTE';unverifiedEvidenceRef:string;claimedSubmitterRef:string;originalSubmissionRequestId:string;claimedFollowUpActorRef:string|null;unverifiedFollowUpNoteRef:string|null};
const uuid=(v:unknown)=>typeof v==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);
const ref=(v:unknown)=>typeof v==='string'&&v.length<=128&&/^test:[A-Za-z0-9_:-]+$/.test(v);
export function exactSupportSource(value:unknown,caseRef:string,c:Cmd,mode:'ACK'|'READ'):boolean{
 if(!value||typeof value!=='object'||Array.isArray(value))return false;
 const x=value as Record<string,unknown>;
 if(!ref(caseRef)||x.contract!=='TRN-TECH-075'||x.syntheticOnly!==true||x.memberAuthenticated!==false||x.complaintLegallySubmitted!==false||x.insurerContacted!==false||x.clinicalReviewCompleted!==false||x.nationalReleaseAllowed!==false||x.caseRef!==caseRef||
 (mode==='READ'?x.outcome!=='READ':!['RecordedUnverified','Replayed'].includes(String(x.outcome)))||
 !Number.isSafeInteger(x.revision)||(x.revision as number)<c.expectedRevision+1||
 (mode==='ACK'&&x.outcome==='RecordedUnverified'&&x.revision!==c.expectedRevision+1)||
 !Number.isSafeInteger(x.followUpCount)||!Array.isArray(x.followUps)||x.followUpCount!==x.followUps.length||
 x.followUps.length!==(x.revision as number)-1||x.memberRef!==c.memberRef||x.evidenceRef!==c.unverifiedEvidenceRef||
 x.claimedSubmitterRef!==c.claimedSubmitterRef||x.topic!==(c.topic==='MEDICAL_SERVICE_COMPLAINT'?'MedicalServiceComplaint':'InsuranceCoverageDispute')||
 x.originalRequestId!==(c.action==='SUBMIT_UNVERIFIED'?c.requestId:c.originalSubmissionRequestId)||
 !uuid(x.originalRequestId)||!uuid(c.requestId)||!ref(c.memberRef)||!ref(c.unverifiedEvidenceRef)||!ref(c.claimedSubmitterRef))return false;
 const ids=new Set<string>([x.originalRequestId as string]);
 for(let i=0;i<x.followUps.length;i++){
  const e=x.followUps[i] as Record<string,unknown>|null;
  if(!e||e.revision!==i+2||!uuid(e.requestId)||ids.has(e.requestId as string)||
    !ref(e.noteRef)||!ref(e.claimedActorRef))return false;
  ids.add(e.requestId as string);
 }
 if(c.action==='SUBMIT_UNVERIFIED')return c.expectedRevision===0&&
 c.originalSubmissionRequestId==='00000000-0000-0000-0000-000000000000'&&c.claimedFollowUpActorRef===null&&c.unverifiedFollowUpNoteRef===null;
 if(c.action!=='FOLLOW_UP_UNVERIFIED'||c.expectedRevision<1||!uuid(c.originalSubmissionRequestId)||c.originalSubmissionRequestId===c.requestId||!ref(c.claimedFollowUpActorRef)||!ref(c.unverifiedFollowUpNoteRef))return false;
 const e=x.followUps[c.expectedRevision-1] as Record<string,unknown>|undefined;
 return !!e&&e.revision===c.expectedRevision+1&&e.requestId===c.requestId&&e.noteRef===c.unverifiedFollowUpNoteRef&&e.claimedActorRef===c.claimedFollowUpActorRef;
}
