// TRN-TECH-092 - intentionally NOT a signed, secret or privileged credential.
// Only exact test: references and the existing ten-field journal command.
// Manual export/import supports loss of one in-memory pending POST across reload.
export type SupportCommand = {
  requestId:string;expectedRevision:number;
  action:'SUBMIT_UNVERIFIED'|'FOLLOW_UP_UNVERIFIED';
  memberRef:string;topic:'MEDICAL_SERVICE_COMPLAINT'|'INSURANCE_COVERAGE_DISPUTE';
  unverifiedEvidenceRef:string;claimedSubmitterRef:string;
  originalSubmissionRequestId:string;
  claimedFollowUpActorRef:string|null;
  unverifiedFollowUpNoteRef:string|null;
};
export type PendingWrite={caseRef:string;command:SupportCommand};
const zero='00000000-0000-0000-0000-000000000000';
const testRef=(s:unknown):s is string=>typeof s==='string'&&
  s.length<=128&&/^test:[A-Za-z0-9_:-]+$/.test(s);
const uuid=(v:unknown):v is string=>typeof v==='string'&&
  /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(v);
const keys=(value:object,names:readonly string[]):boolean=>
  Object.keys(value).length===names.length&&names.every(k=>Object.hasOwn(value,k));
const commandKeys=[
  'requestId','expectedRevision','action','memberRef','topic',
  'unverifiedEvidenceRef','claimedSubmitterRef','originalSubmissionRequestId',
  'claimedFollowUpActorRef','unverifiedFollowUpNoteRef'
] as const;
export function validPending(value:unknown):value is PendingWrite{
  if(!value||typeof value!=='object'||!keys(value,['caseRef','command']))return false;
  const p=value as Partial<PendingWrite>;
  if(!testRef(p.caseRef)||!p.command||typeof p.command!=='object'||
     !keys(p.command,commandKeys))return false;
  const c=p.command;
  if(!uuid(c.requestId)||!Number.isSafeInteger(c.expectedRevision)||
     c.expectedRevision<0||c.expectedRevision>1000000000||
     !testRef(c.memberRef)||!testRef(c.unverifiedEvidenceRef)||
     !testRef(c.claimedSubmitterRef)||
     (c.topic!=='MEDICAL_SERVICE_COMPLAINT'&&c.topic!=='INSURANCE_COVERAGE_DISPUTE'))
    return false;
  if(c.action==='SUBMIT_UNVERIFIED')
    return c.expectedRevision===0&&c.originalSubmissionRequestId===zero&&
      c.claimedFollowUpActorRef===null&&c.unverifiedFollowUpNoteRef===null;
  if(c.action==='FOLLOW_UP_UNVERIFIED')
    return c.expectedRevision>=1&&uuid(c.originalSubmissionRequestId)&&
      c.originalSubmissionRequestId!==c.requestId&&
      testRef(c.claimedFollowUpActorRef)&&testRef(c.unverifiedFollowUpNoteRef);
  return false;
}
const contract='TRN-TECH-092';
export function exportRecovery(p:PendingWrite):string{
  if(!validPending(p))throw Error('INVALID_TEST_COMMAND');
  return JSON.stringify({contract,syntheticOnly:true,caseRef:p.caseRef,command:p.command});
}
export function importRecovery(text:string):PendingWrite|null{
  if(typeof text!=='string'||text.length>4000||text.length<100)return null;
  try{
    const value:unknown=JSON.parse(text);
    if(!value||typeof value!=='object'||!keys(value,['contract','syntheticOnly','caseRef','command']))return null;
    const v=value as {contract?:unknown;syntheticOnly?:unknown;caseRef?:unknown;command?:unknown};
    if(v.contract!==contract||v.syntheticOnly!==true)return null;
    const pending={caseRef:v.caseRef,command:v.command};
    return validPending(pending)?pending:null;
  }catch{return null;}
}
